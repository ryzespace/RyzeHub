using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Errors;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Infrastructure.Clients;

/// <summary>
/// Writes tickets into the RyzeSpace.HelpCenter API. Payload shaping lives in
/// <see cref="TicketPayloadFactory"/> and failure tracking in <see cref="CircuitBreaker"/>.
/// </summary>
public sealed class HelpCenterClient : IDestinationTicketClient
{
    public const string HttpClientName = "ryzehub.destination";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<HelpCenterClient> _logger;
    private readonly FixedWindowRateLimiter _rateLimiter;
    private readonly CircuitBreaker _circuitBreaker;

    public HelpCenterClient(
        HttpClient httpClient,
        ILogger<HelpCenterClient> logger,
        IOptions<DestinationOptions> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _logger = logger;
        _rateLimiter = new FixedWindowRateLimiter(settings.RateLimit, TimeSpan.FromMinutes(1));
        _circuitBreaker = new CircuitBreaker(
            "helpcenter",
            settings.CircuitBreakerFailureThreshold,
            TimeSpan.FromSeconds(settings.CircuitBreakerRecoverySeconds),
            logger);
    }

    public async Task<string> CreateTicketAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        _circuitBreaker.EnsureClosed();
        await _rateLimiter.WaitAsync(cancellationToken);

        _logger.LogInformation("Creating ticket in helpcenter (source_id={TicketId})", ticket.TicketId);

        using var response = await SendAsync(
            () => _httpClient.PostAsJsonAsync("tickets", TicketPayloadFactory.Create(ticket), SerializerOptions, cancellationToken),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _circuitBreaker.RecordFailure();
            throw await TranslateFailureAsync(response, cancellationToken);
        }

        _circuitBreaker.RecordSuccess();

        var result = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
        var newId = result?["ticket_id"]?.GetValue<string>() ?? result?["id"]?.GetValue<string>() ?? string.Empty;

        _logger.LogInformation("Created helpcenter ticket {NewId} (from source {TicketId})", newId, ticket.TicketId);
        return newId;
    }

    public async Task<BatchTransferResult> BatchCreateTicketsAsync(
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        _circuitBreaker.EnsureClosed();
        await _rateLimiter.WaitAsync(cancellationToken);

        _logger.LogInformation("Batch creating {Count} tickets in helpcenter", tickets.Count);

        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(
            () => _httpClient.PostAsJsonAsync("tickets/batch", TicketPayloadFactory.CreateBatch(tickets), SerializerOptions, cancellationToken),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _circuitBreaker.RecordFailure();
            throw await TranslateFailureAsync(response, cancellationToken);
        }

        _circuitBreaker.RecordSuccess();

        var payload = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
        stopwatch.Stop();

        var results = ParseBatchResults(payload);
        var successful = results.Count(item => item.Success);

        return new BatchTransferResult(
            results,
            tickets.Count,
            successful,
            tickets.Count - successful,
            (long)stopwatch.Elapsed.TotalMilliseconds);
    }

    public async Task<string?> CheckTicketExistsAsync(string sourceTicketId, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);

        try
        {
            using var response = await _httpClient.GetAsync(
                $"tickets/lookup?source_ticket_id={Uri.EscapeDataString(sourceTicketId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
            return result?["found"]?.GetValue<bool>() == true
                ? result["ticket_id"]?.GetValue<string>()
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            // A failed lookup must not block the run: worst case the ticket is re-sent.
            return null;
        }
    }

    public async Task<(bool Healthy, TimeSpan Latency)> HealthCheckAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _httpClient.GetAsync("health", cts.Token);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordSuccess();
                return (true, stopwatch.Elapsed);
            }

            _circuitBreaker.RecordFailure();
            return (false, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            stopwatch.Stop();
            _circuitBreaker.RecordFailure();
            return (false, stopwatch.Elapsed);
        }
    }

    /// <summary>Runs a request, converting transport faults into pipeline exceptions.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException exception)
        {
            _circuitBreaker.RecordFailure();
            throw new ConnectionFailedException(exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _circuitBreaker.RecordFailure();
            throw new PipelineTimeoutException(exception.Message);
        }
    }

    private static async Task<PipelineException> TranslateFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
                ?? (double.TryParse(response.Headers.RetryAfter?.ToString(), out var seconds) ? seconds : 60);
            return new RateLimitExceededException((long)retryAfter);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new AuthenticationFailedException($"HelpCenter rejected credentials: {(int)response.StatusCode}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new HttpTransportException($"{(int)response.StatusCode}: {body}");
    }

    private static List<TransferResult> ParseBatchResults(JsonNode? payload)
    {
        var results = new List<TransferResult>();

        if (payload?["results"]?.AsArray() is not { } rawResults)
        {
            return results;
        }

        foreach (var node in rawResults)
        {
            if (node is null)
            {
                continue;
            }

            results.Add(new TransferResult(
                node["ticket_id"]?.GetValue<string>() ?? string.Empty,
                node["success"]?.GetValue<bool>() ?? false,
                node["helpcenter_ticket_id"]?.GetValue<string>(),
                node["error_message"]?.GetValue<string>(),
                DateTimeOffset.UtcNow,
                node["retry_count"]?.GetValue<int>() ?? 0));
        }

        return results;
    }
}
