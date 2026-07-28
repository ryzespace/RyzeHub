using System.Text.Json.Nodes;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform;
using RyzeHub.Domain.Platform;

namespace RyzeHub.UnitTests;

public sealed class HubPlatformTests
{
    private static HubPlatform CreatePlatform() =>
        new(TestSupport.Options(new HubPlatformOptions()), TestSupport.Clock());

    [Fact]
    public void BootstrapContainsAllRequestedModules()
    {
        var snapshot = CreatePlatform().Snapshot();

        snapshot.EnabledModules.Should().HaveCount(17);
        snapshot.EnabledModules.Should().Contain(new[]
        {
            "real_time_event_system",
            "notification_center",
            "audit_log_engine",
            "permission_role_hub",
            "presence_system",
            "device_management",
            "session_manager",
            "api_gateway",
            "distributed_cache",
            "activity_feed",
            "internal_messaging",
            "feature_flags",
            "health_monitoring",
            "telemetry_analytics",
            "security_center",
            "event_bus",
            "file_transfer_service"
        });
    }

    [Fact]
    public void ModuleCatalogDescribesCapabilities()
    {
        var catalog = CreatePlatform().ListModuleCatalog();

        catalog.Should().OnlyContain(module =>
            !string.IsNullOrWhiteSpace(module.Title)
            && !string.IsNullOrWhiteSpace(module.Summary)
            && module.Examples.Count > 0
            && module.Benefits.Count > 0);
    }

    [Fact]
    public void GatewayRoutesIncludeRyzeAuthUpstream()
    {
        CreatePlatform().ListGatewayRoutes()
            .Should().Contain(route => route.UpstreamService == "RyzeAuth" && route.AuthRequired);
    }

    [Fact]
    public void PermissionsInheritFromRoles()
    {
        var platform = CreatePlatform();
        platform.AssignRole("user-1", "Admin");

        var permissions = platform.PermissionsForUser("user-1");

        permissions.Should().Contain("billing:manage");
        platform.HasPermission("user-1", "users:manage").Should().BeTrue();
        platform.HasPermission("user-1", "permissions:manage").Should().BeFalse();
    }

    [Fact]
    public void AccessProfileCombinesRolesAndDirectPermissions()
    {
        var platform = CreatePlatform();
        platform.AssignRole("user-1", "User");
        platform.GrantPermission("user-1", "billing:manage");

        var profile = platform.AccessProfile("user-1");

        profile.Roles.Should().Contain("User");
        profile.DirectPermissions.Should().Contain("billing:manage");
        profile.EffectivePermissions.Should().Contain(new[] { "server:create", "billing:manage" });
    }

    [Fact]
    public void NotificationsCanBeFilteredByUser()
    {
        var platform = CreatePlatform();
        platform.SendNotification("user-1", "A", "first", [NotificationChannel.Email], NotificationPriority.Low, null);
        platform.SendNotification("user-2", "B", "second", [NotificationChannel.Sms], NotificationPriority.High, null);

        platform.ListNotifications("user-1", 10).Should().ContainSingle().Which.Title.Should().Be("A");
        platform.ListNotifications(null, 10).Should().HaveCount(2);
    }

    [Fact]
    public void PublishingEventFansOutNotificationsToRecipients()
    {
        var platform = CreatePlatform();
        platform.RecordSupportStatusUpdate("user-1", "ticket-1", "transferred", [NotificationChannel.Email]);

        platform.ListEvents(10).Should().ContainSingle(evt => evt.Topic == "support.status_updated");
        platform.ListNotifications("user-1", 10).Should().NotBeEmpty();
        platform.ListAuditLogs(null, 10).Should().NotBeEmpty();
        platform.ListActivityFeed("user-1", 10).Should().NotBeEmpty();
    }

    [Fact]
    public void DemoDataProducesSessionsDevicesAndTelemetry()
    {
        var platform = CreatePlatform();
        var snapshot = platform.SeedDemoData("user-001");

        snapshot.Counters.Sessions.Should().BeGreaterThan(0);
        snapshot.Counters.Devices.Should().BeGreaterThan(0);
        snapshot.Counters.Notifications.Should().BeGreaterThan(0);
        snapshot.Telemetry.Logins.Should().Be(1);
        snapshot.Telemetry.ActiveUsers.Should().Be(1);
        platform.ListSecurityAlerts("user-001", 5).Should().NotBeEmpty();
        platform.ListFileTransfers("user-001", 5).Should().NotBeEmpty();
    }

    [Fact]
    public void TerminatingSessionUpdatesHealthAndActiveUsers()
    {
        var platform = CreatePlatform();
        var deviceId = platform.RegisterDevice("user-1", "Laptop", ClientPlatform.Desktop, trusted: true);
        var sessionId = platform.CreateSession("user-1", deviceId, ClientPlatform.Desktop, "203.0.113.1");

        platform.HealthStatus().ActiveSessions.Should().Be(1);

        platform.TerminateSession(sessionId);

        platform.HealthStatus().ActiveSessions.Should().Be(0);
        platform.Snapshot().Telemetry.ActiveUsers.Should().Be(0);
    }

    [Fact]
    public void UnhealthyServiceDegradesPlatformHealth()
    {
        var platform = CreatePlatform();
        platform.UpdateServiceHealth("RyzeAuth", healthy: false, latencyMs: 250);

        var health = platform.HealthStatus();
        health.Status.Should().Be("degraded");
        health.DegradedServices.Should().Be(1);
    }

    [Fact]
    public void CacheStoresAndReadsJsonPayloads()
    {
        var platform = CreatePlatform();
        platform.PutCache("session:user-1", new JsonObject { ["score"] = 92 });

        platform.CacheGet("session:user-1")!["score"]!.GetValue<int>().Should().Be(92);
        platform.ListCacheEntries().Should().ContainSingle();
    }
}
