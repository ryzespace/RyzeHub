using System.Text.Json.Nodes;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform;

/// <summary>
/// Populates a platform instance with representative data so every module has
/// something to show in demos, local runs and CI smoke tests.
/// </summary>
public static class PlatformDemoSeeder
{
    public static HubPlatformSnapshot Seed(IHubPlatform hub, string userId)
    {
        ArgumentNullException.ThrowIfNull(hub);

        SeedAccess(hub, userId);
        SeedIdentity(hub, userId);
        SeedBusinessEvents(hub, userId);
        SeedOperations(hub, userId);

        return hub.Snapshot();
    }

    private static void SeedAccess(IHubPlatform hub, string userId)
    {
        hub.AssignRole(userId, "User");
        hub.AssignRole("support-001", "Support");
        hub.AssignRole("admin-001", "Admin");
        hub.AssignRole("super-001", "SuperAdmin");
        hub.GrantPermission(userId, "billing:view");
        hub.GrantPermission("admin-001", "billing:manage");
    }

    private static void SeedIdentity(IHubPlatform hub, string userId)
    {
        var deviceId = hub.RegisterDevice(userId, "MacBook Pro", ClientPlatform.Desktop, trusted: true);
        hub.CreateSession(userId, deviceId, ClientPlatform.Desktop, "203.0.113.42");
        hub.UpdatePresence(userId, PresenceStatus.Online, deviceId);
        hub.RecordLogin(userId, "203.0.113.42", deviceId);
    }

    private static void SeedBusinessEvents(IHubPlatform hub, string userId)
    {
        hub.RecordSupportStatusUpdate(userId, "ticket-1001", "in_progress",
            [NotificationChannel.MobilePush, NotificationChannel.Email]);

        hub.RecordPaymentCompleted(userId, "payment-4001", 129.99m,
            [NotificationChannel.Email, NotificationChannel.DiscordWebhook]);

        hub.RecordServerActivated(userId, "srv-01",
            [NotificationChannel.MobilePush, NotificationChannel.Desktop]);

        hub.RecordSecurityWarning(userId, "Nowe logowanie z nieznanego urządzenia",
            [NotificationChannel.Email, NotificationChannel.Sms]);

        hub.RecordInternalMessage(userId, "support-001", "Potrzebuję pomocy z serwerem.");
        hub.RecordFileTransfer(userId, "support-log.zip", scanned: true, encrypted: true, versioned: true);
    }

    private static void SeedOperations(IHubPlatform hub, string userId)
    {
        hub.PutCache($"session:{userId}", new JsonObject { ["session_count"] = 1, ["security_score"] = 92 });
        hub.SetFeatureFlag("betaBilling", true, "Nowy billing dla użytkowników beta");
        hub.UpdateServiceHealth("redis-cache", healthy: true, latencyMs: 4);
        hub.UpdateServiceHealth("notification-center", healthy: true, latencyMs: 18);
        hub.RecordActivity(userId, "Utworzono VPS i zsynchronizowano centrum powiadomień",
            new JsonObject { ["source"] = "hub_demo" });
    }
}
