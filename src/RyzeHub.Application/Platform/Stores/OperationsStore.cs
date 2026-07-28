using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IOperationsStore
{
    void SetFeatureFlag(string key, bool enabled, string description, DateTimeOffset now);

    IReadOnlyList<FeatureFlag> FeatureFlags();

    void UpdateServiceHealth(string service, bool healthy, long latencyMs, DateTimeOffset now);

    IReadOnlyList<MonitoredService> Services();

    SecurityAlert RaiseSecurityAlert(SecuritySeverity severity, string title, string description, string? userId, DateTimeOffset now);

    IReadOnlyList<SecurityAlert> SecurityAlerts(string? userId, int limit);

    HubHealthStatus Health(int activeSessions, int queuedNotifications, int emittedEvents);

    TelemetryOverview Telemetry { get; }

    void RecordApiRequest(bool failed);

    void RecordEmittedEvent();

    void RecordDeliveredNotifications(int count);

    void RecordLogin();

    void RecordFileTransfer();

    void SetActiveUsers(long activeUsers);

    int SecurityAlertCount { get; }
}

/// <summary>Feature Flags, Health Monitoring, Telemetry and Security Center.</summary>
public sealed class OperationsStore : IOperationsStore
{
    private static readonly string[] BootstrapServices =
    [
        "api_gateway",
        "event_bus",
        "notification_center",
        "security_center",
        "distributed_cache",
        "RyzeAuth",
        "RyzeSpace.Client",
        "RyzeSpace.HelpCenter"
    ];

    private readonly Dictionary<string, FeatureFlag> _featureFlags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MonitoredService> _services = new(StringComparer.Ordinal);
    private readonly BoundedLog<SecurityAlert> _securityAlerts = new();
    private readonly Lock _gate = new();
    private readonly bool _telemetryEnabled;
    private TelemetryOverview _telemetry = new();

    public OperationsStore(IOptions<HubPlatformOptions> options, ISystemClock clock)
    {
        _telemetryEnabled = options.Value.TelemetryEnabled;
        var now = clock.UtcNow;

        foreach (var flag in DefaultFeatureFlags(now))
        {
            _featureFlags[flag.Key] = flag;
        }

        foreach (var service in BootstrapServices)
        {
            _services[service] = new MonitoredService(service, true, 0, now);
        }
    }

    private static FeatureFlag[] DefaultFeatureFlags(DateTimeOffset now) =>
    [
        new("betaBilling", true, "Nowy billing beta", now),
        new("newDashboard", false, "Nowy dashboard", now),
        new("ryzeAuthApiKeys", true, "Autoryzacja kluczy API przez RyzeAuth", now)
    ];

    public int SecurityAlertCount => _securityAlerts.Count;

    public TelemetryOverview Telemetry
    {
        get { lock (_gate) { return _telemetry; } }
    }

    public void SetFeatureFlag(string key, bool enabled, string description, DateTimeOffset now)
    {
        lock (_gate)
        {
            _featureFlags[key] = new FeatureFlag(key, enabled, description, now);
        }
    }

    public IReadOnlyList<FeatureFlag> FeatureFlags()
    {
        lock (_gate)
        {
            return [.. _featureFlags.Values.OrderBy(flag => flag.Key, StringComparer.Ordinal)];
        }
    }

    public void UpdateServiceHealth(string service, bool healthy, long latencyMs, DateTimeOffset now)
    {
        lock (_gate)
        {
            _services[service] = new MonitoredService(service, healthy, latencyMs, now);
        }

        RecordApiRequest(failed: !healthy);
    }

    public IReadOnlyList<MonitoredService> Services()
    {
        lock (_gate)
        {
            return [.. _services.Values.OrderBy(service => service.Name, StringComparer.Ordinal)];
        }
    }

    public SecurityAlert RaiseSecurityAlert(
        SecuritySeverity severity,
        string title,
        string description,
        string? userId,
        DateTimeOffset now)
    {
        var alert = new SecurityAlert(Guid.NewGuid().ToString(), severity, title, description, userId, now);
        _securityAlerts.Append(alert);
        return alert;
    }

    public IReadOnlyList<SecurityAlert> SecurityAlerts(string? userId, int limit) =>
        _securityAlerts.Recent(limit, alert => userId is null || alert.UserId == userId);

    public HubHealthStatus Health(int activeSessions, int queuedNotifications, int emittedEvents)
    {
        lock (_gate)
        {
            var monitored = _services.Count;
            var healthy = _services.Values.Count(service => service.Healthy);
            var degraded = monitored - healthy;
            var status = degraded == 0 ? "healthy" : healthy > 0 ? "degraded" : "unhealthy";

            return new HubHealthStatus(
                status,
                monitored,
                healthy,
                degraded,
                activeSessions,
                queuedNotifications,
                emittedEvents);
        }
    }

    public void RecordApiRequest(bool failed)
    {
        if (!_telemetryEnabled)
        {
            return;
        }

        lock (_gate)
        {
            _telemetry = _telemetry with
            {
                ApiRequests = _telemetry.ApiRequests + 1,
                ApiErrors = failed ? _telemetry.ApiErrors + 1 : _telemetry.ApiErrors
            };
        }
    }

    public void RecordEmittedEvent()
    {
        lock (_gate)
        {
            _telemetry = _telemetry with { EmittedEvents = _telemetry.EmittedEvents + 1 };
        }
    }

    public void RecordDeliveredNotifications(int count)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _telemetry = _telemetry with { DeliveredNotifications = _telemetry.DeliveredNotifications + count };
        }
    }

    public void RecordLogin()
    {
        lock (_gate)
        {
            _telemetry = _telemetry with { Logins = _telemetry.Logins + 1 };
        }
    }

    public void RecordFileTransfer()
    {
        lock (_gate)
        {
            _telemetry = _telemetry with { FileTransfers = _telemetry.FileTransfers + 1 };
        }
    }

    public void SetActiveUsers(long activeUsers)
    {
        lock (_gate)
        {
            _telemetry = _telemetry with { ActiveUsers = activeUsers };
        }
    }
}
