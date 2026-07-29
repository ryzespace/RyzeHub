using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeAuth.Contracts.Grpc;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Infrastructure.RyzeAuth;

/// <summary>
/// gRPC API-key introspection against RyzeAuth. RyzeHub never stores or hashes keys itself:
/// only a digest of the key is used as a cache key, and only positive results are cached.
/// </summary>
internal sealed class ApiKeyIntrospector(
    ApiKeyIntrospection.ApiKeyIntrospectionClient? grpcClient,
    IRyzeAuthTokenProvider tokenProvider,
    IMemoryCache cache,
    ILogger logger,
    IOptions<RyzeAuthOptions> options)
{
    private readonly RyzeAuthOptions _options = options.Value;

    public async Task<ApiKeyIntrospectionResult> IntrospectAsync(
        string apiKey,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Inactive("api_key is required");
        }

        if (!_options.ApiKeyIntrospectionEnabled || grpcClient is null)
        {
            logger.LogDebug("RyzeAuth API key introspection is disabled");
            return Inactive("introspection disabled");
        }

        var cacheKey = BuildCacheKey(apiKey, requiredScope);
        if (cache.TryGetValue(cacheKey, out ApiKeyIntrospectionResult? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var metadata = new Metadata();
            if (await tokenProvider.GetAccessTokenAsync(cancellationToken) is { Length: > 0 } token)
            {
                metadata.Add("Authorization", $"Bearer {token}");
            }

            var reply = await grpcClient.IntrospectAsync(
                new IntrospectApiKeyRequest { ApiKey = apiKey, RequiredScope = requiredScope },
                metadata,
                DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds),
                cancellationToken);

            var result = new ApiKeyIntrospectionResult(
                reply.Active,
                NullIfEmpty(reply.OrganizationId),
                [.. reply.Scopes],
                NullIfEmpty(reply.KeyId),
                NullIfEmpty(reply.Reason));

            // Never cache a denial: a revoked key must stay denied, and a newly
            // granted key must take effect immediately.
            if (result.Active && _options.IntrospectionCacheSeconds > 0)
            {
                cache.Set(cacheKey, result, TimeSpan.FromSeconds(_options.IntrospectionCacheSeconds));
            }

            return result;
        }
        catch (RpcException exception)
        {
            logger.LogWarning(exception, "RyzeAuth API key introspection failed: {Status}", exception.StatusCode);
            return Inactive($"introspection failed: {exception.Status.Detail}");
        }
    }

    private static string BuildCacheKey(string apiKey, string requiredScope)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{apiKey}|{requiredScope}"));
        return $"ryzeauth:apikey:{Convert.ToHexStringLower(digest)}";
    }

    private static ApiKeyIntrospectionResult Inactive(string reason) => new(false, null, [], null, reason);

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
