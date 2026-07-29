using System.Text.Json.Nodes;

namespace RyzeHub.Domain.Platform;

public sealed record RealtimeEvent(
    string EventId,
    string Topic,
    RealtimeEventType EventType,
    string ActorId,
    string SubjectId,
    IReadOnlyList<string> Recipients,
    JsonNode? Payload,
    DateTimeOffset Timestamp);

public sealed record NotificationEndpoint(NotificationChannel Channel, bool Enabled, string Target);

public sealed record NotificationRecord(
    string NotificationId,
    string UserId,
    string Title,
    string Message,
    IReadOnlyList<NotificationChannel> Channels,
    NotificationPriority Priority,
    JsonNode? Metadata,
    bool Delivered,
    DateTimeOffset CreatedAt);

public sealed record AuditLogEntry(
    string EntryId,
    string ActorId,
    string Action,
    AuditCategory Category,
    string Resource,
    JsonNode? Metadata,
    DateTimeOffset Timestamp);

public sealed record RoleDefinition(string Name, IReadOnlyList<string> Permissions);

public sealed record PresenceRecord(
    string UserId,
    PresenceStatus Status,
    DateTimeOffset LastActivityAt,
    IReadOnlyList<string> ActiveDevices);

public sealed record DeviceRecord(
    string DeviceId,
    string UserId,
    ClientPlatform Platform,
    string DeviceName,
    bool Trusted,
    bool Active,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastSeenAt);

public sealed record SessionRecord(
    string SessionId,
    string UserId,
    string DeviceId,
    ClientPlatform Platform,
    string IpAddress,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed record GatewayRoute(
    string RouteId,
    string Path,
    string UpstreamService,
    bool AuthRequired,
    bool CacheEnabled,
    int RateLimitPerMinute);

public sealed record CacheEntry(string Key, JsonNode? Value, long TtlSeconds, DateTimeOffset StoredAt);

public sealed record ActivityFeedEntry(
    string EntryId,
    string UserId,
    string Description,
    DateTimeOffset Timestamp,
    JsonNode? Metadata);

public sealed record InternalMessage(
    string MessageId,
    string ThreadId,
    string FromUser,
    string ToUser,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record FeatureFlag(string Key, bool Enabled, string Description, DateTimeOffset UpdatedAt);

public sealed record MonitoredService(string Name, bool Healthy, long LatencyMs, DateTimeOffset UpdatedAt);

public sealed record TelemetryOverview
{
    public long ActiveUsers { get; init; }
    public long ApiRequests { get; init; }
    public long ApiErrors { get; init; }
    public long EmittedEvents { get; init; }
    public long DeliveredNotifications { get; init; }
    public long Logins { get; init; }
    public long FileTransfers { get; init; }
}

public sealed record SecurityAlert(
    string AlertId,
    SecuritySeverity Severity,
    string Title,
    string Description,
    string? UserId,
    DateTimeOffset CreatedAt);

public sealed record FileTransferRecord(
    string TransferId,
    string OwnerId,
    string FileName,
    bool Encrypted,
    bool Scanned,
    bool Versioned,
    DateTimeOffset CreatedAt);

public sealed record EventSubscription(string Topic, IReadOnlyList<string> Consumers);

public sealed record PlatformModuleDefinition(
    string Key,
    string Title,
    string Summary,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Benefits);

public sealed record HubHealthStatus(
    string Status,
    int MonitoredServices,
    int HealthyServices,
    int DegradedServices,
    int ActiveSessions,
    int QueuedNotifications,
    int EmittedEvents);

public sealed record HubPlatformCounters(
    int AuditEntries,
    int Notifications,
    int PresenceRecords,
    int Devices,
    int Sessions,
    int ActivityEntries,
    int Messages,
    int CacheEntries,
    int SecurityAlerts,
    int FileTransfers);

public sealed record HubPlatformSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<PlatformModuleDefinition> ModuleCatalog,
    IReadOnlyList<RoleDefinition> Roles,
    IReadOnlyList<FeatureFlag> FeatureFlags,
    IReadOnlyList<EventSubscription> Subscriptions,
    IReadOnlyList<GatewayRoute> GatewayRoutes,
    IReadOnlyList<MonitoredService> Services,
    TelemetryOverview Telemetry,
    HubHealthStatus Health,
    HubPlatformCounters Counters);

public sealed record UserAccessProfile(
    string UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> DirectPermissions,
    IReadOnlyList<string> EffectivePermissions);
