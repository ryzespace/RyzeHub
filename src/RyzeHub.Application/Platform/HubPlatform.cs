using System.Text.Json.Nodes;
using RyzeHub.Application.Platform.Stores;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform;

/// <summary>
/// Facade over the hub platform stores. Holds no state itself: it delegates every
/// module to its own store and composes cross-module workflows (an event plus its
/// audit entry plus its activity trail) through <see cref="IPlatformEventPublisher"/>.
/// </summary>
public sealed class HubPlatform(
    IRealtimeEventStore events,
    INotificationStore notifications,
    IAuditStore audit,
    IAccessControlStore accessControl,
    IIdentityStateStore identityState,
    IGatewayCacheStore gatewayCache,
    IEngagementStore engagement,
    IOperationsStore operations,
    IPlatformEventPublisher publisher,
    ISystemClock clock) : IHubPlatform
{
    // ── Snapshot and health ───────────────────────────────────────────────

    public HubPlatformSnapshot Snapshot() => new(
        clock.UtcNow,
        PlatformModuleCatalog.EnabledModules,
        PlatformModuleCatalog.All,
        accessControl.Roles(),
        operations.FeatureFlags(),
        events.Subscriptions,
        gatewayCache.Routes(),
        operations.Services(),
        operations.Telemetry,
        HealthStatus(),
        new HubPlatformCounters(
            audit.Count,
            notifications.Count,
            identityState.PresenceCount,
            identityState.DeviceCount,
            identityState.SessionCount,
            engagement.ActivityCount,
            engagement.MessageCount,
            gatewayCache.EntryCount,
            operations.SecurityAlertCount,
            engagement.FileTransferCount));

    public HubHealthStatus HealthStatus() =>
        operations.Health(identityState.ActiveSessionCount, notifications.Count, events.Count);

    public IReadOnlyList<PlatformModuleDefinition> ListModuleCatalog() => PlatformModuleCatalog.All;

    // ── Reads ─────────────────────────────────────────────────────────────

    public IReadOnlyList<RealtimeEvent> ListEvents(int limit) => events.Recent(limit);

    public IReadOnlyList<EventSubscription> ListSubscriptions() =>
        [.. events.Subscriptions.OrderBy(subscription => subscription.Topic, StringComparer.Ordinal)];

    public IReadOnlyList<NotificationEndpoint> ListNotificationEndpoints() => notifications.Endpoints();

    public IReadOnlyList<NotificationRecord> ListNotifications(string? userId, int limit) =>
        notifications.Recent(userId, limit);

    public IReadOnlyList<AuditLogEntry> ListAuditLogs(string? actorId, int limit) => audit.Recent(actorId, limit);

    public IReadOnlyList<RoleDefinition> ListRoles() => accessControl.Roles();

    public UserAccessProfile AccessProfile(string userId) => accessControl.AccessProfile(userId);

    public IReadOnlyList<string> PermissionsForUser(string userId) => accessControl.PermissionsForUser(userId);

    public bool HasPermission(string userId, string permission) => accessControl.HasPermission(userId, permission);

    public IReadOnlyList<PresenceRecord> ListPresence(string? userId) => identityState.Presence(userId);

    public IReadOnlyList<DeviceRecord> ListDevices(string? userId) => identityState.Devices(userId);

    public IReadOnlyList<SessionRecord> ListSessions(string? userId) => identityState.Sessions(userId);

    public IReadOnlyList<GatewayRoute> ListGatewayRoutes() => gatewayCache.Routes();

    public IReadOnlyList<CacheEntry> ListCacheEntries() => gatewayCache.Entries();

    public JsonNode? CacheGet(string key) => gatewayCache.Get(key);

    public IReadOnlyList<ActivityFeedEntry> ListActivityFeed(string? userId, int limit) =>
        engagement.Activity(userId, limit);

    public IReadOnlyList<InternalMessage> ListMessages(string? userId, int limit) =>
        engagement.Messages(userId, limit);

    public IReadOnlyList<FeatureFlag> ListFeatureFlags() => operations.FeatureFlags();

    public IReadOnlyList<MonitoredService> ListServices() => operations.Services();

    public IReadOnlyList<SecurityAlert> ListSecurityAlerts(string? userId, int limit) =>
        operations.SecurityAlerts(userId, limit);

    public IReadOnlyList<FileTransferRecord> ListFileTransfers(string? ownerId, int limit) =>
        engagement.FileTransfers(ownerId, limit);

    // ── Notifications ─────────────────────────────────────────────────────

    public NotificationRecord SendNotification(
        string userId,
        string title,
        string message,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        JsonNode? metadata)
    {
        var record = notifications.Send(userId, title, message, channels, priority, metadata, clock.UtcNow);
        operations.RecordDeliveredNotifications(1);
        return record;
    }

    public void SetNotificationEndpoint(NotificationChannel channel, string target, bool enabled) =>
        notifications.SetEndpoint(channel, target, enabled);

    // ── Domain workflows ──────────────────────────────────────────────────

    public void RecordSupportStatusUpdate(
        string userId,
        string ticketId,
        string status,
        IReadOnlyList<NotificationChannel> channels)
    {
        publisher.Publish(
            RealtimeEventType.SupportStatusUpdated,
            "support-system",
            ticketId,
            [userId],
            new JsonObject { ["ticket_id"] = ticketId, ["status"] = status },
            channels,
            NotificationPriority.High,
            $"Status zgłoszenia {ticketId} zmienił się na {status}");

        RecordAudit("support-system", "support_status_updated", AuditCategory.Support, ticketId,
            new JsonObject { ["user_id"] = userId, ["status"] = status });

        RecordActivity(userId, $"Zmieniono status zgłoszenia {ticketId} na {status}",
            new JsonObject { ["ticket_id"] = ticketId });
    }

    public void RecordPaymentCompleted(
        string userId,
        string paymentId,
        decimal amount,
        IReadOnlyList<NotificationChannel> channels)
    {
        publisher.Publish(
            RealtimeEventType.PaymentCompleted,
            userId,
            paymentId,
            [userId, "admin-001"],
            new JsonObject { ["payment_id"] = paymentId, ["amount"] = amount },
            channels,
            NotificationPriority.High,
            $"Płatność {paymentId} została zakończona");

        RecordAudit(userId, "payment_completed", AuditCategory.Finance, paymentId,
            new JsonObject { ["amount"] = amount });

        RecordActivity(userId, $"Zakończono płatność {paymentId} na kwotę {amount:F2}",
            new JsonObject { ["payment_id"] = paymentId, ["amount"] = amount });
    }

    public void RecordServerActivated(string userId, string serverId, IReadOnlyList<NotificationChannel> channels)
    {
        publisher.Publish(
            RealtimeEventType.ServerActivated,
            "infrastructure",
            serverId,
            [userId],
            new JsonObject { ["server_id"] = serverId, ["state"] = "active" },
            channels,
            NotificationPriority.Medium,
            $"Serwer {serverId} został aktywowany");

        RecordActivity(userId, $"Aktywowano serwer {serverId}", new JsonObject { ["server_id"] = serverId });
    }

    public void RecordSecurityWarning(string? userId, string description, IReadOnlyList<NotificationChannel> channels)
    {
        var alert = operations.RaiseSecurityAlert(
            SecuritySeverity.Warning,
            "Ostrzeżenie bezpieczeństwa",
            description,
            userId,
            clock.UtcNow);

        publisher.Publish(
            RealtimeEventType.SecurityWarning,
            "security-center",
            alert.AlertId,
            userId is null ? ["admin-001"] : [userId],
            new JsonObject
            {
                ["alert_id"] = alert.AlertId,
                ["severity"] = alert.Severity.ToString(),
                ["description"] = alert.Description
            },
            channels,
            NotificationPriority.Critical,
            alert.Title);

        RecordAudit("security-center", "security_warning", AuditCategory.Security, alert.AlertId,
            new JsonObject { ["description"] = description });
    }

    public void RecordLogin(string userId, string ipAddress, string? deviceId)
    {
        RecordAudit(userId, "login", AuditCategory.Authentication, userId,
            new JsonObject { ["ip_address"] = ipAddress, ["device_id"] = deviceId });

        RecordActivity(userId, $"Zalogowano do huba z adresu {ipAddress}",
            new JsonObject { ["ip_address"] = ipAddress });

        operations.RecordLogin();

        publisher.Publish(
            RealtimeEventType.LoginRecorded,
            userId,
            userId,
            [userId],
            new JsonObject { ["ip_address"] = ipAddress, ["device_id"] = deviceId },
            [NotificationChannel.Desktop],
            NotificationPriority.Low,
            "Zarejestrowano nowe logowanie");
    }

    // ── Access control ────────────────────────────────────────────────────

    public void AssignRole(string userId, string roleName)
    {
        accessControl.AssignRole(userId, roleName);
        RecordAudit("permission-hub", "role_assigned", AuditCategory.Permission, userId,
            new JsonObject { ["role"] = roleName });
    }

    public void GrantPermission(string userId, string permission)
    {
        accessControl.GrantPermission(userId, permission);

        publisher.Publish(
            RealtimeEventType.PermissionChanged,
            "permission-hub",
            userId,
            ["admin-001"],
            new JsonObject { ["user_id"] = userId, ["permission"] = permission },
            [NotificationChannel.SlackWebhook],
            NotificationPriority.Medium,
            $"Nadano uprawnienie {permission}");

        RecordAudit("permission-hub", "permission_granted", AuditCategory.Permission, userId,
            new JsonObject { ["permission"] = permission });
    }

    // ── Devices, sessions and presence ────────────────────────────────────

    public string RegisterDevice(string userId, string deviceName, ClientPlatform platform, bool trusted)
    {
        var device = identityState.RegisterDevice(userId, deviceName, platform, trusted, clock.UtcNow);

        publisher.Publish(
            RealtimeEventType.DeviceDetected,
            userId,
            device.DeviceId,
            [userId],
            new JsonObject
            {
                ["device_name"] = deviceName,
                ["platform"] = platform.ToString(),
                ["trusted"] = trusted
            },
            [NotificationChannel.Email],
            NotificationPriority.Medium,
            $"Wykryto urządzenie {deviceName}");

        return device.DeviceId;
    }

    public void TrustDevice(string deviceId, bool trusted) =>
        identityState.TrustDevice(deviceId, trusted, clock.UtcNow);

    public void RevokeDevice(string deviceId) => identityState.RevokeDevice(deviceId, clock.UtcNow);

    public string CreateSession(string userId, string deviceId, ClientPlatform platform, string ipAddress)
    {
        var session = identityState.CreateSession(userId, deviceId, platform, ipAddress, clock.UtcNow);
        operations.SetActiveUsers(identityState.ActiveUserCount);

        publisher.Publish(
            RealtimeEventType.SessionCreated,
            userId,
            session.SessionId,
            [userId],
            new JsonObject
            {
                ["device_id"] = deviceId,
                ["platform"] = platform.ToString(),
                ["ip_address"] = ipAddress
            },
            [NotificationChannel.Desktop],
            NotificationPriority.Low,
            "Utworzono nową sesję");

        return session.SessionId;
    }

    public void TerminateSession(string sessionId)
    {
        identityState.TerminateSession(sessionId, clock.UtcNow);
        operations.SetActiveUsers(identityState.ActiveUserCount);
    }

    public void UpdatePresence(string userId, PresenceStatus status, string? deviceId) =>
        identityState.UpdatePresence(userId, status, deviceId, clock.UtcNow);

    // ── Cache, engagement and operations ──────────────────────────────────

    public void PutCache(string key, JsonNode? value) => gatewayCache.Put(key, value, clock.UtcNow);

    public void RecordActivity(string userId, string description, JsonNode? metadata) =>
        engagement.RecordActivity(userId, description, metadata, clock.UtcNow);

    public void RecordInternalMessage(string fromUser, string toUser, string body)
    {
        engagement.RecordMessage(fromUser, toUser, body, clock.UtcNow);

        publisher.Publish(
            RealtimeEventType.NewMessage,
            fromUser,
            toUser,
            [toUser],
            new JsonObject { ["from_user"] = fromUser, ["to_user"] = toUser },
            [NotificationChannel.Desktop, NotificationChannel.MobilePush],
            NotificationPriority.Medium,
            $"Nowa wiadomość od {fromUser}");
    }

    public void SetFeatureFlag(string key, bool enabled, string description)
    {
        operations.SetFeatureFlag(key, enabled, description, clock.UtcNow);

        publisher.Publish(
            RealtimeEventType.FeatureFlagUpdated,
            "feature-flags",
            key,
            ["admin-001"],
            new JsonObject { ["feature_flag"] = key, ["enabled"] = enabled },
            [NotificationChannel.SlackWebhook],
            NotificationPriority.Low,
            $"Zmieniono flagę {key}");
    }

    public void UpdateServiceHealth(string service, bool healthy, long latencyMs)
    {
        operations.UpdateServiceHealth(service, healthy, latencyMs, clock.UtcNow);

        publisher.Publish(
            RealtimeEventType.HealthChanged,
            "health-monitor",
            service,
            ["admin-001"],
            new JsonObject { ["service"] = service, ["healthy"] = healthy, ["latency_ms"] = latencyMs },
            [NotificationChannel.SlackWebhook],
            healthy ? NotificationPriority.Low : NotificationPriority.High,
            $"Zmieniono stan zdrowia usługi {service}");
    }

    public void RecordFileTransfer(string ownerId, string fileName, bool scanned, bool encrypted, bool versioned)
    {
        var record = engagement.RecordFileTransfer(ownerId, fileName, scanned, encrypted, versioned, clock.UtcNow);
        operations.RecordFileTransfer();

        publisher.Publish(
            RealtimeEventType.FileTransferred,
            ownerId,
            record.TransferId,
            [ownerId],
            new JsonObject
            {
                ["file_name"] = fileName,
                ["scanned"] = scanned,
                ["encrypted"] = encrypted,
                ["versioned"] = versioned
            },
            [NotificationChannel.Desktop],
            NotificationPriority.Low,
            $"Przesłano plik {fileName}");
    }

    public void RecordAudit(string actorId, string action, AuditCategory category, string resource, JsonNode? metadata) =>
        audit.Record(actorId, action, category, resource, metadata, clock.UtcNow);

    public HubPlatformSnapshot SeedDemoData(string userId) => PlatformDemoSeeder.Seed(this, userId);
}
