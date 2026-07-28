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

internal enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Writes tickets into the RyzeSpace.HelpCenter API, guarded by a circuit breaker.
/// </summary>
public sealed class HelpCenterClient : IDestinationTicketClient
{
    public const string HttpClientName = "ryzehub.destination";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<HelpCenterClient> _logger;
    private readonly DestinationOptions _options;
    private readonly FixedWindowRateLimiter _rateLimiter;
    private readonly Lock _circuitGate = new();

    private CircuitState _circuitState = CircuitState.Closed;
    private int _failureCount;
    private DateTimeOffset? _lastFailure;

    public HelpCenterClient(
        HttpClient httpClient,
        ILogger<HelpCenterClient> logger,
        IOptions<DestinationOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _rateLimiter = new FixedWindowRateLimiter(_options.RateLimit, TimeSpan.FromMinutes(1));
    }

    private void CheckCircuit()
    {
        lock (_circuitGate)
        {
            if (_circuitState != CircuitState.Open)
            {
                return;
            }

            var recoveryWindow = TimeSpan.FromSeconds(_options.CircuitBreakerRecoverySeconds);
            if (_lastFailure is { } lastFailure && DateTimeOffset.UtcNow - lastFailure > recoveryWindow)
            {
                _circuitState = CircuitState.HalfOpen;
                _logger.LogInformation("Circuit breaker HALF-OPEN for helpcenter");
                return;
            }

            throw new CircuitBreakerOpenException("helpcenter");
        }
    }

    private void RecordSuccess()
    {
        lock (_circuitGate)
        {
            _failureCount = 0;
            _circuitState = CircuitState.Closed;
        }
    }

    private void RecordFailure()
    {
        lock (_circuitGate)
        {
            _failureCount++;
            _lastFailure = DateTimeOffset.UtcNow;

            if (_failureCount >= _options.CircuitBreakerFailureThreshold)
            {
                _logger.LogWarning("Circuit breaker OPEN for helpcenter");
                _circuitState = CircuitState.Open;
            }
        }
    }

    public async Task<string> CreateTicketAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        CheckCircuit();
        await _rateLimiter.WaitAsync(cancellationToken);

        var payload = BuildTicketPayload(ticket);
        _logger.LogInformation("Creating ticket in helpcenter (source_id={TicketId})", ticket.TicketId);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("tickets", payload, SerializerOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            RecordFailure();
            throw new ConnectionFailedException(exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure();
            throw new PipelineTimeoutException(exception.Message);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                RecordSuccess();
                var result = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
                var newId = result?["ticket_id"]?.GetValue<string>() ?? result?["id"]?.GetValue<string>() ?? string.Empty;
                _logger.LogInformation("Created helpcenter ticket {NewId} (from source {TicketId})", newId, ticket.TicketId);
                return newId;
            }

            RecordFailure();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
                    ?? (double.TryParse(response.Headers.RetryAfter?.ToString(), out var seconds) ? seconds : 60);
                throw new RateLimitExceededException((long)retryAfter);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new AuthenticationFailedException($"HelpCenter rejected credentials: {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpTransportException($"{(int)response.StatusCode}: {body}");
        }
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
            return null;
        }
    }

    public async Task<BatchTransferResult> BatchCreateTicketsAsync(
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        CheckCircuit();
        await _rateLimiter.WaitAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var ticketArray = new JsonArray();
        foreach (var ticket in tickets)
        {
            ticketArray.Add(BuildTicketPayload(ticket));
        }

        var payload = new JsonObject { ["tickets"] = ticketArray };

        _logger.LogInformation("Batch creating {Count} tickets in helpcenter", tickets.Count);

        using var response = await _httpClient.PostAsJsonAsync("tickets/batch", payload, SerializerOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            RecordFailure();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpTransportException($"{(int)response.StatusCode}: {body}");
        }

        RecordSuccess();
        var result = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
        stopwatch.Stop();

        var results = new List<TransferResult>();
        if (result?["results"]?.AsArray() is { } rawResults)
        {
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
        }

        var successful = results.Count(item => item.Success);
        return new BatchTransferResult(
            results,
            tickets.Count,
            successful,
            tickets.Count - successful,
            (long)stopwatch.Elapsed.TotalMilliseconds);
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
                RecordSuccess();
                return (true, stopwatch.Elapsed);
            }

            RecordFailure();
            return (false, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            stopwatch.Stop();
            RecordFailure();
            return (false, stopwatch.Elapsed);
        }
    }

    private static JsonObject BuildTicketPayload(Ticket ticket)
    {
        var tags = new JsonArray();
        foreach (var tag in ticket.Tags)
        {
            tags.Add(tag);
        }

        var conversation = new JsonArray();
        foreach (var message in ticket.Conversation)
        {
            conversation.Add(new JsonObject
            {
                ["sender"] = message.Sender,
                ["role"] = message.Role.ToWireValue(),
                ["content"] = message.Content,
                ["timestamp"] = message.Timestamp.ToString("O"),
                ["message_id"] = message.MessageId
            });
        }

        return new JsonObject
        {
            ["ticket_id"] = ticket.TicketId,
            ["ticket_type"] = ticket.TicketType.ToWireValue(),
            ["description"] = ticket.Description,
            ["conversation"] = conversation,
            ["priority"] = ticket.Priority.ToWireValue(),
            ["category"] = ticket.Category,
            ["tags"] = tags,
            ["client_id"] = ticket.ClientId,
            ["client_name"] = ticket.ClientName,
            ["organization_id"] = ticket.OrganizationId,
            ["source"] = "client_dashboard",
            ["source_ticket_id"] = ticket.TicketId,
            ["checksum"] = ticket.Checksum
        };
    }
}
