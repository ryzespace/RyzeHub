using System.Text.Json.Serialization;

namespace RyzeHub.Domain.Platform;

[JsonConverter(typeof(JsonStringEnumConverter<RealtimeEventType>))]
public enum RealtimeEventType
{
    SupportStatusUpdated,
    PaymentCompleted,
    ServerActivated,
    SecurityWarning,
    NewMessage,
    LoginRecorded,
    SessionCreated,
    DeviceDetected,
    PermissionChanged,
    AuditRecorded,
    FileTransferred,
    HealthChanged,
    FeatureFlagUpdated
}

[JsonConverter(typeof(JsonStringEnumConverter<NotificationChannel>))]
public enum NotificationChannel
{
    MobilePush,
    Desktop,
    Email,
    Sms,
    DiscordWebhook,
    SlackWebhook
}

[JsonConverter(typeof(JsonStringEnumConverter<NotificationPriority>))]
public enum NotificationPriority
{
    Low,
    Medium,
    High,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter<PresenceStatus>))]
public enum PresenceStatus
{
    Online,
    Offline,
    Away,
    Busy
}

[JsonConverter(typeof(JsonStringEnumConverter<ClientPlatform>))]
public enum ClientPlatform
{
    Web,
    Mobile,
    Desktop
}

[JsonConverter(typeof(JsonStringEnumConverter<AuditCategory>))]
public enum AuditCategory
{
    Authentication,
    Settings,
    Administration,
    Finance,
    Permission,
    Security,
    Support,
    Messaging,
    Infrastructure
}

[JsonConverter(typeof(JsonStringEnumConverter<SecuritySeverity>))]
public enum SecuritySeverity
{
    Info,
    Warning,
    Critical
}

public static class PlatformEnumExtensions
{
    public static string ToTopic(this RealtimeEventType eventType) => eventType switch
    {
        RealtimeEventType.SupportStatusUpdated => "support.status_updated",
        RealtimeEventType.PaymentCompleted => "payment.completed",
        RealtimeEventType.ServerActivated => "server.activated",
        RealtimeEventType.SecurityWarning => "security.warning",
        RealtimeEventType.NewMessage => "messaging.new_message",
        RealtimeEventType.LoginRecorded => "security.login_recorded",
        RealtimeEventType.SessionCreated => "session.created",
        RealtimeEventType.DeviceDetected => "device.detected",
        RealtimeEventType.PermissionChanged => "permission.changed",
        RealtimeEventType.AuditRecorded => "audit.recorded",
        RealtimeEventType.FileTransferred => "file.transferred",
        RealtimeEventType.HealthChanged => "health.changed",
        _ => "feature_flag.updated"
    };

    public static string ToChannelKey(this NotificationChannel channel) => channel switch
    {
        NotificationChannel.MobilePush => "mobile_push",
        NotificationChannel.Desktop => "desktop",
        NotificationChannel.Email => "email",
        NotificationChannel.Sms => "sms",
        NotificationChannel.DiscordWebhook => "discord_webhook",
        _ => "slack_webhook"
    };

    public static bool TryParseChannel(string value, out NotificationChannel channel)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "mobile":
            case "mobile_push":
            case "push":
                channel = NotificationChannel.MobilePush;
                return true;
            case "desktop":
            case "desktop_notifications":
                channel = NotificationChannel.Desktop;
                return true;
            case "email":
                channel = NotificationChannel.Email;
                return true;
            case "sms":
                channel = NotificationChannel.Sms;
                return true;
            case "discord":
            case "discord_webhook":
            case "discord_webhooks":
                channel = NotificationChannel.DiscordWebhook;
                return true;
            case "slack":
            case "slack_webhook":
            case "slack_webhooks":
                channel = NotificationChannel.SlackWebhook;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    public static bool TryParsePriority(string value, out NotificationPriority priority) =>
        Enum.TryParse(value.Trim(), ignoreCase: true, out priority);
}
