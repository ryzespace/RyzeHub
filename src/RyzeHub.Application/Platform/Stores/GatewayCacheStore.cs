using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IGatewayCacheStore
{
    IReadOnlyList<GatewayRoute> Routes();

    void Put(string key, JsonNode? value, DateTimeOffset now);

    JsonNode? Get(string key);

    IReadOnlyList<CacheEntry> Entries();

    int EntryCount { get; }
}

/// <summary>API Gateway and Distributed Cache: unified entrypoint plus a Redis-style shared cache.</summary>
public sealed class GatewayCacheStore : IGatewayCacheStore
{
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly HubPlatformOptions _options;
    private readonly GatewayRoute[] _routes;

    public GatewayCacheStore(IOptions<HubPlatformOptions> options)
    {
        _options = options.Value;
        var basePath = _options.GatewayBasePath;
        var rateLimit = _options.GatewayRateLimit;

        _routes =
        [
            new(Guid.NewGuid().ToString(), $"{basePath}/client", "RyzeSpace.Client", true, true, rateLimit),
            new(Guid.NewGuid().ToString(), $"{basePath}/helpcenter", "RyzeSpace.HelpCenter", true, true, rateLimit),
            new(Guid.NewGuid().ToString(), $"{basePath}/admin", "RyzeSpace.AdminPanel", true, false, rateLimit),
            new(Guid.NewGuid().ToString(), $"{basePath}/auth", "RyzeAuth", true, false, rateLimit)
        ];
    }

    public int EntryCount
    {
        get { lock (_gate) { return _cache.Count; } }
    }

    public IReadOnlyList<GatewayRoute> Routes() =>
        [.. _routes.OrderBy(route => route.Path, StringComparer.Ordinal)];

    public void Put(string key, JsonNode? value, DateTimeOffset now)
    {
        lock (_gate)
        {
            _cache[key] = new CacheEntry(key, value, _options.CacheTtlSeconds, now);
        }
    }

    public JsonNode? Get(string key)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(key, out var entry) ? entry.Value : null;
        }
    }

    public IReadOnlyList<CacheEntry> Entries()
    {
        lock (_gate)
        {
            return [.. _cache.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal)];
        }
    }
}
