using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Tickets;
using RyzeHub.Domain.Platform;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Pipeline;

/// <summary>
/// Orchestrates fetch -> decrypt -> deduplicate -> transform -> encrypt -> transfer.
/// Delivery, health aggregation and post-transfer side effects live in their own services.
/// </summary>
public sealed class TicketPipeline(
    ILogger<TicketPipeline> logger,
    IOptions<PipelineOptions> pipelineOptions,
    IOptions<SecurityOptions> securityOptions,
    ISourceTicketClient source,
    IDestinationTicketClient destination,
    TicketTransformer transformer,
    ITicketTransferService transferService,
    IPipelineHealthService healthService,
    ITransferOutcomeHandler outcomeHandler,
    IHubPlatform hub,
    PipelineMetrics metrics,
    IErrorDetectionEngine errorDetection,
    ISystemClock clock,
    ITicketEncryptionManager? encryption = null) : ITicketPipeline
{
    private static readonly TicketStatus[] StatusesToFetch =
        [TicketStatus.New, TicketStatus.InProgress, TicketStatus.Waiting];

    private readonly PipelineOptions _pipeline = pipelineOptions.Value;
    private readonly SecurityOptions _security = securityOptions.Value;

    public IHubPlatform Hub => hub;

    public async Task<PipelineRunMetrics> RunOnceAsync(CancellationToken cancellationToken)
    {
        var runMetrics = new PipelineRunMetrics { StartedAt = clock.UtcNow };
        metrics.RecordRun();
        logger.LogInformation("Pipeline run started");

        var tickets = await FetchAsync(runMetrics, cancellationToken);
        if (tickets.Count == 0)
        {
            return Complete(runMetrics);
        }

        ApplyCrypto(tickets, decrypt: true);

        var duplicates = await FindDuplicatesAsync(tickets, cancellationToken);

        var (validTickets, failedResults) = transformer.ProcessBatch(
            tickets,
            _pipeline.AutoCategorize,
            _pipeline.AutoPriority,
            _pipeline.FilterResolved,
            _pipeline.FilterClosed,
            duplicates,
            _security.ChecksumEnabled);

        runMetrics.ValidCount = validTickets.Count;
        runMetrics.FilteredCount = tickets.Count - validTickets.Count - failedResults.Count;
        runMetrics.FailedCount += failedResults.Count;
        metrics.RecordFiltered(runMetrics.FilteredCount);

        if (validTickets.Count == 0)
        {
            logger.LogInformation("No valid tickets after transformation");
            return Complete(runMetrics);
        }

        ApplyCrypto(validTickets, decrypt: false);
        await DeliverAsync(runMetrics, validTickets, cancellationToken);

        errorDetection.RecordMetric("failed_tickets", runMetrics.FailedCount);
        return Complete(runMetrics);
    }

    private async Task<List<Ticket>> FetchAsync(PipelineRunMetrics runMetrics, CancellationToken cancellationToken)
    {
        logger.LogInformation("Step 1: Fetching tickets from client dashboard");

        var tickets = (await source.FetchAllPagesAsync(StatusesToFetch, cancellationToken)).ToList();
        hub.UpdateServiceHealth("RyzeSpace.Client", healthy: true, latencyMs: 0);

        runMetrics.FetchedCount = tickets.Count;
        metrics.RecordFetched(tickets.Count);

        if (tickets.Count == 0)
        {
            logger.LogInformation("No tickets to process");
        }

        return tickets;
    }

    /// <summary>Encrypts or decrypts the sensitive fields of every ticket, tolerating per-ticket failures.</summary>
    private void ApplyCrypto(IReadOnlyList<Ticket> tickets, bool decrypt)
    {
        if (encryption is null)
        {
            return;
        }

        var operation = decrypt ? "decrypt" : "encrypt";
        logger.LogInformation("Applying {Operation} to {Count} tickets", operation, tickets.Count);

        foreach (var ticket in tickets)
        {
            try
            {
                if (decrypt)
                {
                    encryption.DecryptTicket(ticket);
                }
                else
                {
                    encryption.EncryptTicket(ticket);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to {Operation} ticket {TicketId}", operation, ticket.TicketId);
                errorDetection.DetectError(exception.Message, $"pipeline.{operation}");
            }
        }
    }

    private async Task<HashSet<string>> FindDuplicatesAsync(
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        var duplicates = new HashSet<string>(StringComparer.Ordinal);

        if (!_pipeline.Deduplicate)
        {
            return duplicates;
        }

        logger.LogInformation("Checking for duplicates");

        foreach (var ticket in tickets)
        {
            var existingId = await destination.CheckTicketExistsAsync(ticket.TicketId, cancellationToken);
            if (existingId is not null)
            {
                duplicates.Add(ticket.TicketId);
                logger.LogInformation("Duplicate found: {TicketId} -> {ExistingId}", ticket.TicketId, existingId);
            }
        }

        logger.LogInformation("Found {Count} already transferred tickets", duplicates.Count);
        return duplicates;
    }

    private async Task DeliverAsync(
        PipelineRunMetrics runMetrics,
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Transferring {Count} tickets to helpcenter", tickets.Count);

        var stopwatch = Stopwatch.StartNew();
        var results = await transferService.TransferAsync(tickets, cancellationToken);
        stopwatch.Stop();

        var transferMs = (long)stopwatch.Elapsed.TotalMilliseconds;
        metrics.RecordTransferDuration(transferMs);
        errorDetection.RecordMetric("transfer_duration_ms", transferMs);
        hub.UpdateServiceHealth("RyzeSpace.HelpCenter", healthy: true, latencyMs: transferMs);

        foreach (var result in results)
        {
            if (result.Success)
            {
                runMetrics.TransferredCount++;
                metrics.RecordTransferred(1);
                await outcomeHandler.OnSuccessAsync(result, tickets, cancellationToken);
            }
            else
            {
                runMetrics.FailedCount++;
                metrics.RecordFailed(1);

                if (result.ErrorMessage is { } error)
                {
                    runMetrics.Errors.Add($"{result.TicketId}: {error}");
                }

                outcomeHandler.OnFailure(result, transferMs);
            }
        }
    }

    private PipelineRunMetrics Complete(PipelineRunMetrics runMetrics)
    {
        runMetrics.Finish(clock.UtcNow);

        logger.LogInformation(
            "Pipeline finished. Fetched: {Fetched}, Transferred: {Transferred}, Failed: {Failed}, Duration: {Duration:F2}s",
            runMetrics.FetchedCount,
            runMetrics.TransferredCount,
            runMetrics.FailedCount,
            runMetrics.DurationSeconds);

        return runMetrics;
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

    public Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken) =>
        healthService.CheckAsync(cancellationToken);

    public HubPlatformSnapshot HubSnapshot() => hub.Snapshot();

    public HubPlatformSnapshot SeedHubDemo(string userId)
    {
        var deviceId = hub.RegisterDevice(userId, "RyzeHub Control Center", ClientPlatform.Desktop, trusted: true);
        hub.CreateSession(userId, deviceId, ClientPlatform.Desktop, "198.51.100.10");
        hub.UpdatePresence(userId, PresenceStatus.Online, deviceId);
        return hub.SeedDemoData(userId);
    }
}
