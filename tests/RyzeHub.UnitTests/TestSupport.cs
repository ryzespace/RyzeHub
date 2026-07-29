using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform;
using RyzeHub.Application.Platform.Stores;

namespace RyzeHub.UnitTests;

internal static class TestSupport
{
    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;

    public static IOptions<T> Options<T>(T value) where T : class => Microsoft.Extensions.Options.Options.Create(value);

    /// <summary>Deterministic 32 byte base64 key for crypto tests.</summary>
    public static string TestKey(byte fill = 0) => Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());

    public static ISystemClock Clock() => new FixedClock(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Builds a fully wired hub platform over real stores, mirroring the production
    /// composition root without needing a DI container.
    /// </summary>
    public static HubPlatform CreateHubPlatform(HubPlatformOptions? options = null, ISystemClock? clock = null)
    {
        var resolvedOptions = Options(options ?? new HubPlatformOptions());
        var resolvedClock = clock ?? Clock();

        var events = new RealtimeEventStore(resolvedOptions);
        var notifications = new NotificationStore(resolvedOptions);
        var audit = new AuditStore();
        var accessControl = new AccessControlStore();
        var identityState = new IdentityStateStore();
        var gatewayCache = new GatewayCacheStore(resolvedOptions);
        var engagement = new EngagementStore();
        var operations = new OperationsStore(resolvedOptions, resolvedClock);
        var publisher = new PlatformEventPublisher(events, notifications, operations, resolvedClock);

        return new HubPlatform(
            events,
            notifications,
            audit,
            accessControl,
            identityState,
            gatewayCache,
            engagement,
            operations,
            publisher,
            resolvedClock);
    }

    public sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
