using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform;

/// <summary>
/// In-memory runtime backing every hub platform module: realtime events, notifications,
/// audit, RBAC, presence, devices, sessions, gateway, cache, activity, messaging,
/// feature flags, monitoring, telemetry, security and file transfer.
/// </summary>
public sealed class HubPlatform : IHubPlatform
{
    private readonly HubPlatformOptions _options;
    private readonly ISystemClock _clock;
    private readonly Lock _gate = new();

    private readonly List<RealtimeEvent> _events = [];
    private readonly List<NotificationEndpoint> _notificationEndpoints = [];
    private readonly List<NotificationRecord> _notifications = [];
    private readonly List<AuditLogEntry> _auditLogs = [];
    private readonly Dictionary<string, RoleDefinition> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _userRoles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _directPermissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PresenceRecord> _presence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceRecord> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FeatureFlag> _featureFlags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MonitoredService> _services = new(StringComparer.Ordinal);
    private readonly List<ActivityFeedEntry> _activityFeed = [];
    private readonly List<InternalMessage> _messages = [];
    private readonly List<SecurityAlert> _securityAlerts = [];
    private readonly List<FileTransferRecord> _fileTransfers = [];
    private List<GatewayRoute> _gatewayRoutes = [];
    private List<EventSubscription> _subscriptions = [];
    private TelemetryOverview _telemetry = new();

    public HubPlatform(IOptions<HubPlatformOptions> options, ISystemClock clock)
    {
        _options = options.Value;
        _clock = clock;
        BootstrapStandardTopology();
    }

    private void BootstrapStandardTopology()
    {
        lock (_gate)
        {
            if (_roles.Count != 0)
            {
                return;
            }

            _notificationEndpoints.AddRange(
            [
                new(NotificationChannel.MobilePush, true, "mobile://push"),
                new(NotificationChannel.Desktop, true, "desktop://notifications"),
                new(NotificationChannel.Email, true, "smtp://primary"),
                new(NotificationChannel.Sms, true, "sms://primary"),
                new(NotificationChannel.DiscordWebhook, true, "discord://hub-notifications"),
                new(NotificationChannel.SlackWebhook, true, "slack://hub-notifications")
            ]);

            foreach (var role in PlatformModuleCatalog.DefaultRoles)
            {
                _roles[role.Name] = role;
            }

            foreach (var flag in DefaultFeatureFlags())
            {
                _featureFlags[flag.Key] = flag;
            }

            _gatewayRoutes =
            [
                new(Guid.NewGuid().ToString(), $"{_options.GatewayBasePath}/client", "RyzeSpace.Client", true, true, _options.GatewayRateLimit),
                new(Guid.NewGuid().ToString(), $"{_options.GatewayBasePath}/helpcenter", "RyzeSpace.HelpCenter", true, true, _options.GatewayRateLimit),
                new(Guid.NewGuid().ToString(), $"{_options.GatewayBasePath}/admin", "RyzeSpace.AdminPanel", true, false, _options.GatewayRateLimit),
                new(Guid.NewGuid().ToString(), $"{_options.GatewayBasePath}/auth", "RyzeAuth", true, false, _options.GatewayRateLimit)
            ];

            string[] services =
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

            foreach (var service in services)
            {
                _services[service] = new MonitoredService(service, true, 0, _clock.UtcNow);
            }

            _subscriptions =
            [
                new("payment.completed", ["billing_service", "notification_center", "admin_dashboard", "user_dashboard"]),
                new("support.status_updated", ["helpcenter", "client_dashboard", "notification_center"]),
                new("security.warning", ["security_center", "notification_center", "audit_log_engine", "ryzeauth"]),
                new("server.activated", ["infrastructure_service", "notification_center", "activity_feed"]),
                new("security.login_recorded", ["security_center", "ryzeauth", "activity_feed"])
            ];
        }
    }

    private IEnumerable<FeatureFlag> DefaultFeatureFlags() =>
    [
        new("betaBilling", true, "Nowy billing beta", _clock.UtcNow),
        new("newDashboard", false, "Nowy dashboard", _clock.UtcNow),
        new("ryzeAuthApiKeys", true, "Autoryzacja kluczy API przez RyzeAuth", _clock.UtcNow)
    ];

    public HubPlatformSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new HubPlatformSnapshot(
                _clock.UtcNow,
                PlatformModuleCatalog.EnabledModules,
                PlatformModuleCatalog.All,
                [.. _roles.Values.OrderBy(role => role.Name, StringComparer.Ordinal)],
                [.. _featureFlags.Values.OrderBy(flag => flag.Key, StringComparer.Ordinal)],
                [.. _subscriptions],
                [.. _gatewayRoutes],
                [.. _services.Values.OrderBy(service => service.Name, StringComparer.Ordinal)],
                _telemetry,
                BuildHealthStatus(),
                new HubPlatformCounters(
                    _auditLogs.Count,
                    _notifications.Count,
                    _presence.Count,
                    _devices.Count,
                    _sessions.Count,
                    _activityFeed.Count,
                    _messages.Count,
                    _cache.Count,
                    _securityAlerts.Count,
                    _fileTransfers.Count));
        }
    }

    public HubHealthStatus HealthStatus()
    {
        lock (_gate)
        {
            return BuildHealthStatus();
        }
    }

    private HubHealthStatus BuildHealthStatus()
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
            _sessions.Values.Count(session => session.Active),
            _notifications.Count,
            _events.Count);
    }

    public IReadOnlyList<PlatformModuleDefinition> ListModuleCatalog() => PlatformModuleCatalog.All;

    public IReadOnlyList<RealtimeEvent> ListEvents(int limit)
    {
        lock (_gate)
        {
            return TakeRecent(_events, limit);
        }
    }

    public IReadOnlyList<NotificationEndpoint> ListNotificationEndpoints()
    {
        lock (_gate)
        {
            return [.. _notificationEndpoints.OrderBy(endpoint => endpoint.Channel.ToChannelKey(), StringComparer.Ordinal)];
        }
    }

    public IReadOnlyList<NotificationRecord> ListNotifications(string? userId, int limit)
    {
        lock (_gate)
        {
            return TakeRecent(_notifications, limit, record => userId is null || record.UserId == userId);
        }
    }

    public IReadOnlyList<AuditLogEntry> ListAuditLogs(string? actorId, int limit)
    {
        lock (_gate)
        {
            return TakeRecent(_auditLogs, limit, entry => actorId is null || entry.ActorId == actorId);
        }
    }

    public IReadOnlyList<RoleDefinition> ListRoles()
    {
        lock (_gate)
        {
            return [.. _roles.Values.OrderBy(role => role.Name, StringComparer.Ordinal)];
        }
    }

    public UserAccessProfile AccessProfile(string userId)
    {
        lock (_gate)
        {
            string[] roles = _userRoles.TryGetValue(userId, out var assigned)
                ? [.. assigned.Order(StringComparer.Ordinal)]
                : [];

            string[] direct = _directPermissions.TryGetValue(userId, out var permissions)
                ? [.. permissions.Order(StringComparer.Ordinal)]
                : [];

            return new UserAccessProfile(userId, roles, direct, ResolvePermissions(userId));
        }
    }

    public IReadOnlyList<PresenceRecord> ListPresence(string? userId)
    {
        lock (_gate)
        {
            return
            [
                .. _presence.Values
                    .Where(record => userId is null || record.UserId == userId)
                    .OrderByDescending(record => record.LastActivityAt)
            ];
        }
    }

    public IReadOnlyList<DeviceRecord> ListDevices(string? userId)
    {
        lock (_gate)
        {
            return
            [
                .. _devices.Values
                    .Where(record => userId is null || record.UserId == userId)
                    .OrderByDescending(record => record.LastSeenAt)
            ];
        }
    }

    public IReadOnlyList<SessionRecord> ListSessions(string? userId)
    {
        lock (_gate)
        {
            return
            [
                .. _sessions.Values
                    .Where(record => userId is null || record.UserId == userId)
                    .OrderByDescending(record => record.LastSeenAt)
            ];
        }
    }

    public IReadOnlyList<GatewayRoute> ListGatewayRoutes()
    {
        lock (_gate)
        {
            return [.. _gatewayRoutes.OrderBy(route => route.Path, StringComparer.Ordinal)];
        }
    }

    public IReadOnlyList<CacheEntry> ListCacheEntries()
    {
        lock (_gate)
        {
            return [.. _cache.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal)];
        }
    }

    public IReadOnlyList<ActivityFeedEntry> ListActivityFeed(string? userId, int limit)
    {
        lock (_gate)
        {
            return TakeRecent(_activityFeed, limit, entry => userId is null || entry.UserId == userId);
        }
    }

    public IReadOnlyList<InternalMessage> ListMessages(string? userId, int limit)
    {
        lock (_gate)
        {
            return TakeRecent(
                _messages,
                limit,
                message => userId is null || message.FromUser == userId || message.ToUser == userId);
        }
    }

    public IReadOnlyList<FeatureFlag> ListFeatureFlags()
    {
        lock (_gate)
        {
            return [.. _featureFlags.Values.OrderBy(flag => flag.Key, StringComparer.Ordinal)];
        }
    }

    public IReadOnlyList<MonitoredService> ListServices()
    {
        lock (_gate)
        {
            return [.. _services.Values.OrderBy(service => service.Name, StringComparer.Ordinal)];
        }
    }

    public IReadOnlyList<SecurityAlert> ListSecurityAlerts(string? userId, int limit)
    {
        lock (_gate)
        {
            return TakeRecent(_securityAlerts, limit, alert => userId is null || alert.UserId == userId);
        }
    }

    public IReadOnlyList<FileTransferRecord> ListFileTransfers(string? ownerId, int limit)
    {
        lock (_gate)
        {
            return TakeRecent(_fileTransfers, limit, record => ownerId is null || record.OwnerId == ownerId);
        }
    }

    public IReadOnlyList<EventSubscription> ListSubscriptions()
    {
        lock (_gate)
        {
            return [.. _subscriptions.OrderBy(subscription => subscription.Topic, StringComparer.Ordinal)];
        }
    }

    public NotificationRecord SendNotification(
        string userId,
        string title,
        string message,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        JsonNode? metadata)
    {
        var record = new NotificationRecord(
            Guid.NewGuid().ToString(),
            userId,
            title,
            message,
            channels,
            priority,
            metadata,
            Delivered: true,
            _clock.UtcNow);

        lock (_gate)
        {
            AppendNotification(record);
        }

        return record;
    }

    public void SetNotificationEndpoint(NotificationChannel channel, string target, bool enabled)
    {
        lock (_gate)
        {
            var index = _notificationEndpoints.FindIndex(endpoint => endpoint.Channel == channel);
            var updated = new NotificationEndpoint(channel, enabled, target);

            if (index >= 0)
            {
                _notificationEndpoints[index] = updated;
            }
            else
            {
                _notificationEndpoints.Add(updated);
            }
        }
    }

    public HubPlatformSnapshot SeedDemoData(string userId)
    {
        AssignRole(userId, "User");
        AssignRole("support-001", "Support");
        AssignRole("admin-001", "Admin");
        AssignRole("super-001", "SuperAdmin");
        GrantPermission(userId, "billing:view");
        GrantPermission("admin-001", "billing:manage");

        var deviceId = RegisterDevice(userId, "MacBook Pro", ClientPlatform.Desktop, trusted: true);
        CreateSession(userId, deviceId, ClientPlatform.Desktop, "203.0.113.42");
        UpdatePresence(userId, PresenceStatus.Online, deviceId);
        RecordLogin(userId, "203.0.113.42", deviceId);

        RecordSupportStatusUpdate(userId, "ticket-1001", "in_progress",
            [NotificationChannel.MobilePush, NotificationChannel.Email]);
        RecordPaymentCompleted(userId, "payment-4001", 129.99m,
            [NotificationChannel.Email, NotificationChannel.DiscordWebhook]);
        RecordServerActivated(userId, "srv-01",
            [NotificationChannel.MobilePush, NotificationChannel.Desktop]);
        RecordSecurityWarning(userId, "Nowe logowanie z nieznanego urządzenia",
            [NotificationChannel.Email, NotificationChannel.Sms]);
        RecordInternalMessage(userId, "support-001", "Potrzebuję pomocy z serwerem.");
        RecordFileTransfer(userId, "support-log.zip", scanned: true, encrypted: true, versioned: true);
        PutCache($"session:{userId}", new JsonObject { ["session_count"] = 1, ["security_score"] = 92 });
        SetFeatureFlag("betaBilling", true, "Nowy billing dla użytkowników beta");
        UpdateServiceHealth("redis-cache", true, 4);
        UpdateServiceHealth("notification-center", true, 18);
        RecordActivity(userId, "Utworzono VPS i zsynchronizowano centrum powiadomień",
            new JsonObject { ["source"] = "hub_demo" });

        return Snapshot();
    }

    public void RecordSupportStatusUpdate(
        string userId,
        string ticketId,
        string status,
        IReadOnlyList<NotificationChannel> channels)
    {
        PublishEvent(
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
        PublishEvent(
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
        PublishEvent(
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
        var alert = new SecurityAlert(
            Guid.NewGuid().ToString(),
            SecuritySeverity.Warning,
            "Ostrzeżenie bezpieczeństwa",
            description,
            userId,
            _clock.UtcNow);

        lock (_gate)
        {
            _securityAlerts.Add(alert);
        }

        PublishEvent(
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

        lock (_gate)
        {
            _telemetry = _telemetry with { Logins = _telemetry.Logins + 1 };
        }

        PublishEvent(
            RealtimeEventType.LoginRecorded,
            userId,
            userId,
            [userId],
            new JsonObject { ["ip_address"] = ipAddress, ["device_id"] = deviceId },
            [NotificationChannel.Desktop],
            NotificationPriority.Low,
            "Zarejestrowano nowe logowanie");
    }

    public void AssignRole(string userId, string roleName)
    {
        lock (_gate)
        {
            if (!_userRoles.TryGetValue(userId, out var roles))
            {
                roles = new HashSet<string>(StringComparer.Ordinal);
                _userRoles[userId] = roles;
            }

            roles.Add(roleName);
        }

        RecordAudit("permission-hub", "role_assigned", AuditCategory.Permission, userId,
            new JsonObject { ["role"] = roleName });
    }

    public void GrantPermission(string userId, string permission)
    {
        lock (_gate)
        {
            if (!_directPermissions.TryGetValue(userId, out var permissions))
            {
                permissions = new HashSet<string>(StringComparer.Ordinal);
                _directPermissions[userId] = permissions;
            }

            permissions.Add(permission);
        }

        PublishEvent(
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

    public IReadOnlyList<string> PermissionsForUser(string userId)
    {
        lock (_gate)
        {
            return ResolvePermissions(userId);
        }
    }

    public bool HasPermission(string userId, string permission)
    {
        lock (_gate)
        {
            return ResolvePermissions(userId).Contains(permission, StringComparer.Ordinal);
        }
    }

    private string[] ResolvePermissions(string userId)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);

        if (_userRoles.TryGetValue(userId, out var assignedRoles))
        {
            foreach (var roleName in assignedRoles)
            {
                if (_roles.TryGetValue(roleName, out var role))
                {
                    foreach (var permission in role.Permissions)
                    {
                        permissions.Add(permission);
                    }
                }
            }
        }

        if (_directPermissions.TryGetValue(userId, out var direct))
        {
            foreach (var permission in direct)
            {
                permissions.Add(permission);
            }
        }

        return [.. permissions.Order(StringComparer.Ordinal)];
    }

    public string RegisterDevice(string userId, string deviceName, ClientPlatform platform, bool trusted)
    {
        var deviceId = Guid.NewGuid().ToString();
        var now = _clock.UtcNow;

        lock (_gate)
        {
            _devices[deviceId] = new DeviceRecord(deviceId, userId, platform, deviceName, trusted, true, now, now);
        }

        PublishEvent(
            RealtimeEventType.DeviceDetected,
            userId,
            deviceId,
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

        return deviceId;
    }

    public void TrustDevice(string deviceId, bool trusted)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                _devices[deviceId] = device with { Trusted = trusted, LastSeenAt = _clock.UtcNow };
            }
        }
    }

    public void RevokeDevice(string deviceId)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                _devices[deviceId] = device with { Active = false, LastSeenAt = _clock.UtcNow };
            }
        }
    }

    public string CreateSession(string userId, string deviceId, ClientPlatform platform, string ipAddress)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = _clock.UtcNow;

        lock (_gate)
        {
            _sessions[sessionId] = new SessionRecord(sessionId, userId, deviceId, platform, ipAddress, true, now, now);
            _telemetry = _telemetry with { ActiveUsers = CountActiveUsers() };
        }

        PublishEvent(
            RealtimeEventType.SessionCreated,
            userId,
            sessionId,
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

        return sessionId;
    }

    public void TerminateSession(string sessionId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                _sessions[sessionId] = session with { Active = false, LastSeenAt = _clock.UtcNow };
            }

            _telemetry = _telemetry with { ActiveUsers = CountActiveUsers() };
        }
    }

    public void UpdatePresence(string userId, PresenceStatus status, string? deviceId)
    {
        lock (_gate)
        {
            List<string> devices = _presence.TryGetValue(userId, out var existing)
                ? [.. existing.ActiveDevices]
                : [];

            if (deviceId is not null && !devices.Contains(deviceId, StringComparer.Ordinal))
            {
                devices.Add(deviceId);
            }

            _presence[userId] = new PresenceRecord(userId, status, _clock.UtcNow, devices);
        }
    }

    public void PutCache(string key, JsonNode? value)
    {
        lock (_gate)
        {
            _cache[key] = new CacheEntry(key, value, _options.CacheTtlSeconds, _clock.UtcNow);
        }
    }

    public JsonNode? CacheGet(string key)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(key, out var entry) ? entry.Value : null;
        }
    }

    public void RecordActivity(string userId, string description, JsonNode? metadata)
    {
        lock (_gate)
        {
            _activityFeed.Add(new ActivityFeedEntry(
                Guid.NewGuid().ToString(),
                userId,
                description,
                _clock.UtcNow,
                metadata));
        }
    }

    public void RecordInternalMessage(string fromUser, string toUser, string body)
    {
        lock (_gate)
        {
            _messages.Add(new InternalMessage(
                Guid.NewGuid().ToString(),
                $"{fromUser}:{toUser}",
                fromUser,
                toUser,
                body,
                _clock.UtcNow));
        }

        PublishEvent(
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
        lock (_gate)
        {
            _featureFlags[key] = new FeatureFlag(key, enabled, description, _clock.UtcNow);
        }

        PublishEvent(
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
        lock (_gate)
        {
            _services[service] = new MonitoredService(service, healthy, latencyMs, _clock.UtcNow);

            if (_options.TelemetryEnabled)
            {
                _telemetry = _telemetry with
                {
                    ApiRequests = _telemetry.ApiRequests + 1,
                    ApiErrors = healthy ? _telemetry.ApiErrors : _telemetry.ApiErrors + 1
                };
            }
        }

        PublishEvent(
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
        var record = new FileTransferRecord(
            Guid.NewGuid().ToString(),
            ownerId,
            fileName,
            encrypted,
            scanned,
            versioned,
            _clock.UtcNow);

        lock (_gate)
        {
            _fileTransfers.Add(record);
            _telemetry = _telemetry with { FileTransfers = _telemetry.FileTransfers + 1 };
        }

        PublishEvent(
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

    public void RecordAudit(string actorId, string action, AuditCategory category, string resource, JsonNode? metadata)
    {
        lock (_gate)
        {
            _auditLogs.Add(new AuditLogEntry(
                Guid.NewGuid().ToString(),
                actorId,
                action,
                category,
                resource,
                metadata,
                _clock.UtcNow));
        }
    }

    private void PublishEvent(
        RealtimeEventType eventType,
        string actorId,
        string subjectId,
        IReadOnlyList<string> recipients,
        JsonNode? payload,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        string notificationMessage)
    {
        var topic = eventType.ToTopic();
        var now = _clock.UtcNow;

        lock (_gate)
        {
            _events.Add(new RealtimeEvent(
                Guid.NewGuid().ToString(),
                topic,
                eventType,
                actorId,
                subjectId,
                recipients,
                payload?.DeepClone(),
                now));

            TrimTo(_events, _options.MaxEvents);

            _telemetry = _telemetry with
            {
                EmittedEvents = _telemetry.EmittedEvents + 1,
                ApiRequests = _options.TelemetryEnabled ? _telemetry.ApiRequests + 1 : _telemetry.ApiRequests
            };

            foreach (var recipient in recipients)
            {
                AppendNotification(new NotificationRecord(
                    Guid.NewGuid().ToString(),
                    recipient,
                    topic,
                    notificationMessage,
                    channels,
                    priority,
                    payload?.DeepClone(),
                    Delivered: true,
                    now));
            }
        }
    }

    private void AppendNotification(NotificationRecord record)
    {
        _notifications.Add(record);
        TrimTo(_notifications, _options.MaxNotifications);
        _telemetry = _telemetry with { DeliveredNotifications = _telemetry.DeliveredNotifications + 1 };
    }

    private long CountActiveUsers() =>
        _sessions.Values.Where(session => session.Active).Select(session => session.UserId).Distinct(StringComparer.Ordinal).Count();

    private static void TrimTo<T>(List<T> items, int maxCount)
    {
        if (maxCount > 0 && items.Count > maxCount)
        {
            items.RemoveRange(0, items.Count - maxCount);
        }
    }

    private static IReadOnlyList<T> TakeRecent<T>(List<T> items, int limit, Func<T, bool>? predicate = null)
    {
        if (limit <= 0)
        {
            return [];
        }

        var result = new List<T>(Math.Min(limit, items.Count));
        for (var index = items.Count - 1; index >= 0 && result.Count < limit; index--)
        {
            if (predicate is null || predicate(items[index]))
            {
                result.Add(items[index]);
            }
        }

        return result;
    }
}
