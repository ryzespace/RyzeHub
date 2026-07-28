using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeAuth.Contracts.Grpc;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Infrastructure.RyzeAuth;

/// <summary>
/// Talks to the RyzeAuth control plane:
/// gRPC <c>ApiKeyIntrospection</c> for scoped API keys, REST <c>/internal/tokens/introspect</c>
/// for opaque/JWT tokens and the security audit sink for forwarded RyzeHub events.
/// </summary>
public sealed class RyzeAuthClient : IRyzeAuthClient
{
    public const string HttpClientName = "ryzehub.ryzeauth";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ApiKeyIntrospection.ApiKeyIntrospectionClient? _grpcClient;
    private readonly IRyzeAuthTokenProvider _tokenProvider;
    private readonly IMemoryCache _cache;
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
        _cache = cache;
        _logger = logger;
        _options = options.Value;
        _grpcClient = grpcClient;
    }

    public async Task<ApiKeyIntrospectionResult> IntrospectApiKeyAsync(
        string apiKey,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiKeyIntrospectionResult(false, null, [], null, "api_key is required");
        }

        if (!_options.ApiKeyIntrospectionEnabled || _grpcClient is null)
        {
            _logger.LogDebug("RyzeAuth API key introspection is disabled");
            return new ApiKeyIntrospectionResult(false, null, [], null, "introspection disabled");
        }

        // Cache only the positive result, keyed by a digest so raw keys never sit in memory.
        var cacheKey = $"ryzeauth:apikey:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{apiKey}|{requiredScope}")))}";

        if (_cache.TryGetValue(cacheKey, out ApiKeyIntrospectionResult? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var metadata = await BuildCallMetadataAsync(cancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);

            var reply = await _grpcClient.IntrospectAsync(
                new IntrospectApiKeyRequest { ApiKey = apiKey, RequiredScope = requiredScope },
                metadata,
                deadline,
                cancellationToken);

            var result = new ApiKeyIntrospectionResult(
                reply.Active,
                string.IsNullOrEmpty(reply.OrganizationId) ? null : reply.OrganizationId,
                [.. reply.Scopes],
                string.IsNullOrEmpty(reply.KeyId) ? null : reply.KeyId,
                string.IsNullOrEmpty(reply.Reason) ? null : reply.Reason);

            if (result.Active && _options.IntrospectionCacheSeconds > 0)
            {
                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(_options.IntrospectionCacheSeconds));
            }

            return result;
        }
        catch (RpcException exception)
        {
            _logger.LogWarning(exception, "RyzeAuth API key introspection failed: {Status}", exception.StatusCode);
            return new ApiKeyIntrospectionResult(false, null, [], null, $"introspection failed: {exception.Status.Detail}");
        }
    }

    public async Task<TokenIntrospectionResult> IntrospectTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384)
        {
            return new TokenIntrospectionResult(false, null, null, [], [], null);
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
                return new TokenIntrospectionResult(false, null, null, [], [], null);
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonNode>(SerializerOptions, cancellationToken);
            if (payload is null || payload["active"]?.GetValue<bool>() != true)
            {
                return new TokenIntrospectionResult(false, null, null, [], [], null);
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
            return new TokenIntrospectionResult(false, null, null, [], [], null);
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
            _logger.LogDebug(exception, "Could not forward audit event {EventType}", auditEvent.EventType);
        }
    }

    public async Task<(bool Healthy, TimeSpan Latency, bool JwksReachable, bool GrpcReachable)> HealthCheckAsync(
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var jwksReachable = false;
        var grpcReachable = false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var discovery = await _httpClient.GetAsync(
                $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration",
                cts.Token);
            jwksReachable = discovery.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            jwksReachable = false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var live = await _httpClient.GetAsync("health/live", cts.Token);
            grpcReachable = live.IsSuccessStatusCode && _grpcClient is not null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            grpcReachable = false;
        }

        stopwatch.Stop();
        return (jwksReachable || grpcReachable, stopwatch.Elapsed, jwksReachable, grpcReachable);
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<Metadata> BuildCallMetadataAsync(CancellationToken cancellationToken)
    {
        var metadata = new Metadata();
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        if (!string.IsNullOrEmpty(token))
        {
            metadata.Add("Authorization", $"Bearer {token}");
        }

        return metadata;
    }

    private static IReadOnlyList<string> ReadStringList(JsonNode? node) => node?.AsArray() is { } array
        ? [.. array.Select(item => item?.GetValue<string>()).OfType<string>()]
        : [];

    private static IReadOnlyList<string> SplitScopes(string? scope) => string.IsNullOrWhiteSpace(scope)
        ? []
        : [.. scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
