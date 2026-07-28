using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Tickets;
using RyzeHub.Domain.Platform;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Pipeline;

/// <summary>
/// Orchestrates fetch -> decrypt -> deduplicate -> transform -> encrypt -> transfer.
/// </summary>
public sealed class TicketPipeline(
    ILogger<TicketPipeline> logger,
    IOptions<PipelineOptions> pipelineOptions,
    IOptions<DestinationOptions> destinationOptions,
    IOptions<SecurityOptions> securityOptions,
    ISourceTicketClient source,
    IDestinationTicketClient destination,
    IAuditLogger audit,
    TicketTransformer transformer,
    IHubPlatform hub,
    PipelineMetrics metrics,
    IErrorDetectionEngine errorDetection,
    ISystemClock clock,
    // Optional: absent when no encryption key is configured, or when RyzeAuth is disabled.
    ITicketEncryptionManager? encryption = null,
    IRyzeAuthClient? ryzeAuth = null) : ITicketPipeline
{
    private readonly PipelineOptions _pipeline = pipelineOptions.Value;
    private readonly DestinationOptions _destination = destinationOptions.Value;
    private readonly SecurityOptions _security = securityOptions.Value;

    public IHubPlatform Hub => hub;

    public async Task<PipelineRunMetrics> RunOnceAsync(CancellationToken cancellationToken)
    {
        var runMetrics = new PipelineRunMetrics { StartedAt = clock.UtcNow };
        metrics.RecordRun();

        logger.LogInformation("Pipeline run started");

        logger.LogInformation("Step 1: Fetching tickets from client dashboard");
        var tickets = (await source.FetchAllPagesAsync(StatusesToFetch, cancellationToken)).ToList();
        hub.UpdateServiceHealth("RyzeSpace.Client", healthy: true, latencyMs: 0);
        runMetrics.FetchedCount = tickets.Count;
        metrics.RecordFetched(tickets.Count);

        if (tickets.Count == 0)
        {
            logger.LogInformation("No tickets to process");
            runMetrics.Finish(clock.UtcNow);
            return runMetrics;
        }

        if (encryption is not null)
        {
            logger.LogInformation("Step 2: Decrypting ticket data");
            foreach (var ticket in tickets)
            {
                try
                {
                    encryption.DecryptTicket(ticket);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to decrypt ticket {TicketId}", ticket.TicketId);
                    errorDetection.DetectError(exception.Message, "pipeline.decrypt");
                }
            }
        }

        var alreadyTransferred = new HashSet<string>(StringComparer.Ordinal);
        if (_pipeline.Deduplicate)
        {
            logger.LogInformation("Step 3: Checking for duplicates");
            foreach (var ticket in tickets)
            {
                var existingId = await destination.CheckTicketExistsAsync(ticket.TicketId, cancellationToken);
                if (existingId is not null)
                {
                    alreadyTransferred.Add(ticket.TicketId);
                    logger.LogInformation("Duplicate found: {TicketId} -> {ExistingId}", ticket.TicketId, existingId);
                }
            }

            logger.LogInformation("Found {Count} already transferred tickets", alreadyTransferred.Count);
        }

        logger.LogInformation("Step 4: Transforming tickets (validate + enrich)");
        var (validTickets, failedResults) = transformer.ProcessBatch(
            tickets,
            _pipeline.AutoCategorize,
            _pipeline.AutoPriority,
            _pipeline.FilterResolved,
            _pipeline.FilterClosed,
            alreadyTransferred,
            _security.ChecksumEnabled);

        runMetrics.ValidCount = validTickets.Count;
        runMetrics.FilteredCount = tickets.Count - validTickets.Count - failedResults.Count;
        runMetrics.FailedCount += failedResults.Count;
        metrics.RecordFiltered(runMetrics.FilteredCount);

        if (validTickets.Count == 0)
        {
            logger.LogInformation("No valid tickets after transformation");
            runMetrics.Finish(clock.UtcNow);
            return runMetrics;
        }

        if (encryption is not null)
        {
            logger.LogInformation("Step 5: Encrypting sensitive data before transfer");
            foreach (var ticket in validTickets)
            {
                try
                {
                    encryption.EncryptTicket(ticket);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to encrypt ticket {TicketId}", ticket.TicketId);
                    errorDetection.DetectError(exception.Message, "pipeline.encrypt");
                }
            }
        }

        logger.LogInformation("Step 6: Transferring {Count} tickets to helpcenter", validTickets.Count);
        var transferStopwatch = Stopwatch.StartNew();
        var transferResults = await TransferTicketsAsync(validTickets, cancellationToken);
        transferStopwatch.Stop();

        var transferMs = (long)transferStopwatch.Elapsed.TotalMilliseconds;
        metrics.RecordTransferDuration(transferMs);
        errorDetection.RecordMetric("transfer_duration_ms", transferMs);
        hub.UpdateServiceHealth("RyzeSpace.HelpCenter", healthy: true, latencyMs: transferMs);

        foreach (var result in transferResults)
        {
            if (result.Success)
            {
                await HandleSuccessAsync(runMetrics, validTickets, result, cancellationToken);
            }
            else
            {
                HandleFailure(runMetrics, result, transferMs);
            }
        }

        errorDetection.RecordMetric("failed_tickets", runMetrics.FailedCount);
        runMetrics.Finish(clock.UtcNow);

        logger.LogInformation(
            "Pipeline finished. Fetched: {Fetched}, Transferred: {Transferred}, Failed: {Failed}, Duration: {Duration:F2}s",
            runMetrics.FetchedCount,
            runMetrics.TransferredCount,
            runMetrics.FailedCount,
            runMetrics.DurationSeconds);

        return runMetrics;
    }

    private async Task HandleSuccessAsync(
        PipelineRunMetrics runMetrics,
        IReadOnlyList<Ticket> tickets,
        TransferResult result,
        CancellationToken cancellationToken)
    {
        runMetrics.TransferredCount++;
        metrics.RecordTransferred(1);
        audit.LogTransfer(result.TicketId, "client_dashboard", result.HelpCenterTicketId ?? "unknown");

        await source.MarkAsTransferredAsync(result.TicketId, result.HelpCenterTicketId ?? string.Empty, cancellationToken);

        var ticket = tickets.FirstOrDefault(candidate => candidate.TicketId == result.TicketId);
        if (ticket is null)
        {
            return;
        }

        var userId = string.IsNullOrEmpty(ticket.ClientId) ? "unknown-user" : ticket.ClientId;

        hub.UpdatePresence(userId, PresenceStatus.Online, $"ticket-{ticket.TicketId}");
        hub.RecordSupportStatusUpdate(
            userId,
            ticket.TicketId,
            "transferred",
            [NotificationChannel.MobilePush, NotificationChannel.Desktop, NotificationChannel.Email]);
        hub.RecordActivity(
            userId,
            $"Przeniesiono zgłoszenie {ticket.TicketId} do HelpCenter",
            new JsonObject
            {
                ["ticket_id"] = ticket.TicketId,
                ["helpcenter_ticket_id"] = result.HelpCenterTicketId
            });

        if (ryzeAuth is not null)
        {
            await ForwardAuditAsync(ticket, result, cancellationToken);
        }
    }

    private async Task ForwardAuditAsync(Ticket ticket, TransferResult result, CancellationToken cancellationToken)
    {
        try
        {
            await ryzeAuth!.ForwardAuditEventAsync(
                new RyzeAuthAuditEvent(
                    "ryzehub.ticket_transferred",
                    "success",
                    clock.UtcNow,
                    ticket.ClientId,
                    ticket.OrganizationId,
                    ticket.TicketId,
                    new JsonObject
                    {
                        ["ticket_id"] = ticket.TicketId,
                        ["helpcenter_ticket_id"] = result.HelpCenterTicketId,
                        ["category"] = ticket.Category,
                        ["priority"] = ticket.Priority.ToWireValue()
                    }),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to forward transfer audit for {TicketId}", ticket.TicketId);
        }
    }

    private void HandleFailure(PipelineRunMetrics runMetrics, TransferResult result, long transferMs)
    {
        runMetrics.FailedCount++;
        metrics.RecordFailed(1);

        if (result.ErrorMessage is not { } error)
        {
            return;
        }

        runMetrics.Errors.Add($"{result.TicketId}: {error}");
        errorDetection.DetectError(error, "pipeline.transfer");
        hub.UpdateServiceHealth("RyzeSpace.HelpCenter", healthy: false, latencyMs: transferMs);
        hub.RecordSecurityWarning(
            userId: null,
            $"Błąd transferu zgłoszenia {result.TicketId}: {error}",
            [NotificationChannel.SlackWebhook, NotificationChannel.DiscordWebhook]);
    }

    public async Task RunContinuousAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting continuous pipeline (poll every {Interval}s)", _pipeline.PollIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pipeline run error");
                errorDetection.DetectError(exception.Message, "pipeline.run");
            }

            logger.LogInformation("Waiting {Interval}s until next run", _pipeline.PollIntervalSeconds);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_pipeline.PollIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<IReadOnlyList<TransferResult>> TransferTicketsAsync(
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        var results = new List<TransferResult>(tickets.Count);

        foreach (var ticket in tickets)
        {
            var lastError = string.Empty;
            var success = false;
            string? helpCenterId = null;
            var attempts = 0;

            for (var attempt = 0; attempt < Math.Max(1, _destination.MaxRetries); attempt++)
            {
                attempts = attempt + 1;
                try
                {
                    helpCenterId = await destination.CreateTicketAsync(ticket, cancellationToken);
                    success = true;
                    break;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    lastError = exception.Message;
                    logger.LogWarning(
                        "Transfer attempt {Attempt}/{MaxRetries} failed for ticket {TicketId}: {Error}",
                        attempt + 1,
                        _destination.MaxRetries,
                        ticket.TicketId,
                        lastError);

                    if (attempt + 1 >= _destination.MaxRetries)
                    {
                        break;
                    }

                    // Exponential backoff between retries.
                    var delaySeconds = _destination.RetryDelaySeconds * Math.Pow(2, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }

            results.Add(new TransferResult(
                ticket.TicketId,
                success,
                helpCenterId,
                success ? null : lastError,
                clock.UtcNow,
                attempts));
        }

        return results;
    }

    private static IReadOnlyList<TicketStatus> StatusesToFetch =>
        [TicketStatus.New, TicketStatus.InProgress, TicketStatus.Waiting];

    public async Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken)
    {
        var (sourceHealthy, sourceLatency) = await SafeHealthCheckAsync(source.HealthCheckAsync, cancellationToken);
        var (destinationHealthy, destinationLatency) = await SafeHealthCheckAsync(destination.HealthCheckAsync, cancellationToken);

        hub.UpdateServiceHealth("RyzeSpace.Client", sourceHealthy, (long)sourceLatency.TotalMilliseconds);
        hub.UpdateServiceHealth("RyzeSpace.HelpCenter", destinationHealthy, (long)destinationLatency.TotalMilliseconds);

        AuthHealthStatus? authHealth = null;
        if (ryzeAuth is not null)
        {
            try
            {
                var (healthy, latency, jwks, grpc) = await ryzeAuth.HealthCheckAsync(cancellationToken);
                hub.UpdateServiceHealth("RyzeAuth", healthy, (long)latency.TotalMilliseconds);
                authHealth = new AuthHealthStatus(
                    healthy ? "up" : "down",
                    "configured",
                    jwks,
                    grpc,
                    (long)latency.TotalMilliseconds,
                    clock.UtcNow);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RyzeAuth health check failed");
                hub.UpdateServiceHealth("RyzeAuth", healthy: false, latencyMs: 0);
                authHealth = new AuthHealthStatus("down", "configured", false, false, 0, clock.UtcNow);
            }
        }

        var overallHealthy = sourceHealthy && destinationHealthy && authHealth?.Status != "down";
        var overallStatus = overallHealthy
            ? "healthy"
            : sourceHealthy || destinationHealthy
                ? "degraded"
                : "unhealthy";

        return new HealthStatus(
            overallStatus,
            clock.UtcNow,
            new ServiceHealth("client_dashboard", sourceHealthy ? "up" : "down", (long)sourceLatency.TotalMilliseconds, clock.UtcNow),
            new ServiceHealth("helpcenter", destinationHealthy ? "up" : "down", (long)destinationLatency.TotalMilliseconds, clock.UtcNow),
            new PipelineHealth(overallStatus, PipelineMetrics.UptimeSeconds, 0, 0.0),
            hub.HealthStatus(),
            authHealth);
    }

    private static async Task<(bool Healthy, TimeSpan Latency)> SafeHealthCheckAsync(
        Func<CancellationToken, Task<(bool, TimeSpan)>> check,
        CancellationToken cancellationToken)
    {
        try
        {
            return await check(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, TimeSpan.Zero);
        }
    }

    public HubPlatformSnapshot HubSnapshot() => hub.Snapshot();

    public HubPlatformSnapshot SeedHubDemo(string userId)
    {
        var deviceId = hub.RegisterDevice(userId, "RyzeHub Control Center", ClientPlatform.Desktop, trusted: true);
        hub.CreateSession(userId, deviceId, ClientPlatform.Desktop, "198.51.100.10");
        hub.UpdatePresence(userId, PresenceStatus.Online, deviceId);
        return hub.SeedDemoData(userId);
    }
}
