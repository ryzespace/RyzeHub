using System.Text.Json.Nodes;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application;

public interface IHubPlatform
{
    HubPlatformSnapshot Snapshot();

    HubHealthStatus HealthStatus();

    IReadOnlyList<PlatformModuleDefinition> ListModuleCatalog();

    IReadOnlyList<RealtimeEvent> ListEvents(int limit);

    IReadOnlyList<NotificationEndpoint> ListNotificationEndpoints();

    IReadOnlyList<NotificationRecord> ListNotifications(string? userId, int limit);

    IReadOnlyList<AuditLogEntry> ListAuditLogs(string? actorId, int limit);

    IReadOnlyList<RoleDefinition> ListRoles();

    UserAccessProfile AccessProfile(string userId);

    IReadOnlyList<PresenceRecord> ListPresence(string? userId);

    IReadOnlyList<DeviceRecord> ListDevices(string? userId);

    IReadOnlyList<SessionRecord> ListSessions(string? userId);

    IReadOnlyList<GatewayRoute> ListGatewayRoutes();

    IReadOnlyList<CacheEntry> ListCacheEntries();

    IReadOnlyList<ActivityFeedEntry> ListActivityFeed(string? userId, int limit);

    IReadOnlyList<InternalMessage> ListMessages(string? userId, int limit);

    IReadOnlyList<FeatureFlag> ListFeatureFlags();

    IReadOnlyList<MonitoredService> ListServices();

    IReadOnlyList<SecurityAlert> ListSecurityAlerts(string? userId, int limit);

    IReadOnlyList<FileTransferRecord> ListFileTransfers(string? ownerId, int limit);

    IReadOnlyList<EventSubscription> ListSubscriptions();

    NotificationRecord SendNotification(
        string userId,
        string title,
        string message,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        JsonNode? metadata);

    void SetNotificationEndpoint(NotificationChannel channel, string target, bool enabled);

    HubPlatformSnapshot SeedDemoData(string userId);

    void RecordSupportStatusUpdate(string userId, string ticketId, string status, IReadOnlyList<NotificationChannel> channels);

    void RecordPaymentCompleted(string userId, string paymentId, decimal amount, IReadOnlyList<NotificationChannel> channels);

    void RecordServerActivated(string userId, string serverId, IReadOnlyList<NotificationChannel> channels);

    void RecordSecurityWarning(string? userId, string description, IReadOnlyList<NotificationChannel> channels);

    void RecordLogin(string userId, string ipAddress, string? deviceId);

    void AssignRole(string userId, string roleName);

    void GrantPermission(string userId, string permission);

    IReadOnlyList<string> PermissionsForUser(string userId);

    bool HasPermission(string userId, string permission);

    string RegisterDevice(string userId, string deviceName, ClientPlatform platform, bool trusted);

    void TrustDevice(string deviceId, bool trusted);

    void RevokeDevice(string deviceId);

    string CreateSession(string userId, string deviceId, ClientPlatform platform, string ipAddress);

    void TerminateSession(string sessionId);

    void UpdatePresence(string userId, PresenceStatus status, string? deviceId);

    void PutCache(string key, JsonNode? value);

    JsonNode? CacheGet(string key);

    void RecordActivity(string userId, string description, JsonNode? metadata);

    void RecordInternalMessage(string fromUser, string toUser, string body);

    void SetFeatureFlag(string key, bool enabled, string description);

    void UpdateServiceHealth(string service, bool healthy, long latencyMs);

    void RecordFileTransfer(string ownerId, string fileName, bool scanned, bool encrypted, bool versioned);

    void RecordAudit(string actorId, string action, AuditCategory category, string resource, JsonNode? metadata);
}
