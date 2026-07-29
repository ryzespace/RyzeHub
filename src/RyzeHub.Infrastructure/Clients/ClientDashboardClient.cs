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
/// Reads tickets from the RyzeSpace.Client dashboard API.
/// </summary>
public sealed class ClientDashboardClient : ISourceTicketClient
{
    public const string HttpClientName = "ryzehub.source";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<ClientDashboardClient> _logger;
    private readonly SourceOptions _options;
    private readonly FixedWindowRateLimiter _rateLimiter;

    public ClientDashboardClient(
        HttpClient httpClient,
        ILogger<ClientDashboardClient> logger,
        IOptions<SourceOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _rateLimiter = new FixedWindowRateLimiter(_options.RateLimit, TimeSpan.FromMinutes(1));
    }

    public async Task<IReadOnlyList<Ticket>> FetchTicketsAsync(
        IReadOnlyList<TicketStatus>? statuses,
        int page,
        CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);

        var query = new List<string>
        {
            $"page={page}",
            $"per_page={_options.BatchSize}"
        };

        if (statuses is { Count: > 0 })
        {
            query.Add($"status={Uri.EscapeDataString(string.Join(',', statuses.Select(status => status.ToWireValue())))}");
        }

        var requestUri = $"tickets?{string.Join('&', query)}";
        _logger.LogInformation("Fetching tickets from {RequestUri} (page={Page})", requestUri, page);

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to fetch tickets: {StatusCode} - {Body}", response.StatusCode, body);
            throw new HttpTransportException($"{(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
        var rawTickets = payload?["tickets"]?.AsArray() ?? payload?["data"]?.AsArray();

        if (rawTickets is null)
        {
            return [];
        }

        var tickets = new List<Ticket>(rawTickets.Count);
        foreach (var node in rawTickets)
        {
            try
            {
                if (node.Deserialize<Ticket>(SerializerOptions) is { } ticket)
                {
                    tickets.Add(ticket);
                }
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Skipping malformed ticket payload");
            }
        }

        _logger.LogInformation("Fetched {Count} tickets from client dashboard", tickets.Count);
        return tickets;
    }

    public async Task<IReadOnlyList<Ticket>> FetchAllPagesAsync(
        IReadOnlyList<TicketStatus>? statuses,
        CancellationToken cancellationToken)
    {
        var allTickets = new List<Ticket>();
        var page = 1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await FetchTicketsAsync(statuses, page, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            allTickets.AddRange(batch);

            if (batch.Count < _options.BatchSize)
            {
                break;
            }

            page++;
        }

        _logger.LogInformation("Fetched {Count} total tickets across all pages", allTickets.Count);
        return allTickets;
    }

    public async Task<Ticket> FetchTicketByIdAsync(string ticketId, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);

        using var response = await _httpClient.GetAsync($"tickets/{Uri.EscapeDataString(ticketId)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TicketNotFoundException(ticketId);
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Ticket>(SerializerOptions, cancellationToken)
            ?? throw new SerializationFailedException($"Empty ticket payload for {ticketId}");
    }

    public async Task<bool> MarkAsTransferredAsync(
        string ticketId,
        string helpCenterTicketId,
        CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);

        var payload = new JsonObject
        {
            ["status"] = "transferred",
            ["helpcenter_ticket_id"] = helpCenterTicketId
        };

        try
        {
            using var response = await _httpClient.PatchAsJsonAsync(
                $"tickets/{Uri.EscapeDataString(ticketId)}/status",
                payload,
                SerializerOptions,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Marked ticket {TicketId} as transferred", ticketId);
                return true;
            }

            _logger.LogWarning("Failed to mark ticket {TicketId} as transferred: {StatusCode}", ticketId, response.StatusCode);
            return false;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Failed to mark ticket {TicketId} as transferred", ticketId);
            return false;
        }
    }

    public async Task<int> GetTotalCountAsync(IReadOnlyList<TicketStatus>? statuses, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);

        var query = new List<string> { "count_only=true" };
        if (statuses is { Count: > 0 })
        {
            query.Add($"status={Uri.EscapeDataString(string.Join(',', statuses.Select(status => status.ToWireValue())))}");
        }

        using var response = await _httpClient.GetAsync($"tickets?{string.Join('&', query)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
        return payload?["total"]?.GetValue<int>() ?? 0;
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
            return (response.IsSuccessStatusCode, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            stopwatch.Stop();
            return (false, stopwatch.Elapsed);
        }
    }
}
