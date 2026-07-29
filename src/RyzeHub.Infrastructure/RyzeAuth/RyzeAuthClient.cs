using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeAuth.Contracts.Grpc;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Infrastructure.RyzeAuth;

/// <summary>
/// Talks to the RyzeAuth control plane: REST token introspection, audit forwarding and
/// health probes. gRPC API-key introspection is delegated to <see cref="ApiKeyIntrospector"/>.
/// </summary>
public sealed class RyzeAuthClient : IRyzeAuthClient
{
    public const string HttpClientName = "ryzehub.ryzeauth";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IRyzeAuthTokenProvider _tokenProvider;
    private readonly ApiKeyIntrospector _apiKeyIntrospector;
    private readonly ILogger<RyzeAuthClient> _logger;
    private readonly RyzeAuthOptions _options;

    public RyzeAuthClient(
        HttpClient httpClient,
        IRyzeAuthTokenProvider tokenProvider,
        IMemoryCache cache,
        ILogger<RyzeAuthClient> logger,
        IOptions<RyzeAuthOptions> options,
        ApiKeyIntrospection.ApiKeyIntrospectionClient? grpcClient = null)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _options = options.Value;
        _apiKeyIntrospector = new ApiKeyIntrospector(grpcClient, tokenProvider, cache, logger, options);
    }

    public Task<ApiKeyIntrospectionResult> IntrospectApiKeyAsync(
        string apiKey,
        string requiredScope,
        CancellationToken cancellationToken) =>
        _apiKeyIntrospector.IntrospectAsync(apiKey, requiredScope, cancellationToken);

    public async Task<TokenIntrospectionResult> IntrospectTokenAsync(string token, CancellationToken cancellationToken)
    {
        // Mirrors the RyzeAuth endpoint's own bound, so oversized input is rejected locally.
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384)
        {
            return InactiveToken();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/tokens/introspect")
        {
            Content = JsonContent.Create(new { token }, options: SerializerOptions)
        };

        await AuthorizeAsync(request, cancellationToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RyzeAuth token introspection returned {StatusCode}", response.StatusCode);
                return InactiveToken();
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);

            if (payload?["active"]?.GetValue<bool>() != true)
            {
                return InactiveToken();
            }

            return new TokenIntrospectionResult(
                Active: true,
                Subject: payload["sub"]?.GetValue<string>(),
                PreferredUsername: payload["preferred_username"]?.GetValue<string>(),
                Roles: ReadStringList(payload["roles"]),
                Scopes: SplitScopes(payload["scope"]?.GetValue<string>()),
                OrganizationId: payload["organization_id"]?.GetValue<string>());
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "RyzeAuth token introspection failed");
            return InactiveToken();
        }
    }

    public async Task ForwardAuditEventAsync(RyzeAuthAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (!_options.ForwardAuditEvents)
        {
            return;
        }

        var payload = new JsonObject
        {
            ["eventType"] = auditEvent.EventType,
            ["outcome"] = auditEvent.Outcome,
            ["occurredAt"] = auditEvent.OccurredAt.ToString("O"),
            ["subjectId"] = auditEvent.SubjectId,
            ["organizationId"] = auditEvent.OrganizationId,
            ["correlationId"] = auditEvent.CorrelationId,
            ["metadata"] = auditEvent.Metadata?.DeepClone()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/audit/events")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        await AuthorizeAsync(request, cancellationToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "RyzeAuth audit sink returned {StatusCode} for {EventType}",
                    response.StatusCode,
                    auditEvent.EventType);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Audit forwarding is best effort and must never fail a pipeline run.
            _logger.LogDebug(exception, "Could not forward audit event {EventType}", auditEvent.EventType);
        }
    }

    public async Task<(bool Healthy, TimeSpan Latency, bool JwksReachable, bool GrpcReachable)> HealthCheckAsync(
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var jwksReachable = await ProbeAsync(
            $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration",
            cancellationToken);

        var apiReachable = await ProbeAsync("health/live", cancellationToken);

        stopwatch.Stop();
        return (jwksReachable || apiReachable, stopwatch.Elapsed, jwksReachable, apiReachable);
    }

    private async Task<bool> ProbeAsync(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await _httpClient.GetAsync(requestUri, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (await _tokenProvider.GetAccessTokenAsync(cancellationToken) is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static TokenIntrospectionResult InactiveToken() => new(false, null, null, [], [], null);

    private static IReadOnlyList<string> ReadStringList(JsonNode? node) => node?.AsArray() is { } array
        ? [.. array.Select(item => item?.GetValue<string>()).OfType<string>()]
        : [];

    private static IReadOnlyList<string> SplitScopes(string? scope) => string.IsNullOrWhiteSpace(scope)
        ? []
        : [.. scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
