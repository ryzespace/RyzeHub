using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface INotificationStore
{
    NotificationRecord Send(
        string userId,
        string title,
        string message,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        JsonNode? metadata,
        DateTimeOffset now);

    IReadOnlyList<NotificationRecord> Recent(string? userId, int limit);

    IReadOnlyList<NotificationEndpoint> Endpoints();

    void SetEndpoint(NotificationChannel channel, string target, bool enabled);

    int Count { get; }

    long DeliveredCount { get; }
}

/// <summary>Notification Center: one delivery hub for push, desktop, email, SMS, Discord and Slack.</summary>
public sealed class NotificationStore : INotificationStore
{
    private readonly BoundedLog<NotificationRecord> _notifications;
    private readonly List<NotificationEndpoint> _endpoints;
    private readonly Lock _endpointGate = new();
    private long _delivered;

    public NotificationStore(IOptions<HubPlatformOptions> options)
    {
        _notifications = new BoundedLog<NotificationRecord>(options.Value.MaxNotifications);
        _endpoints =
        [
            new(NotificationChannel.MobilePush, true, "mobile://push"),
            new(NotificationChannel.Desktop, true, "desktop://notifications"),
            new(NotificationChannel.Email, true, "smtp://primary"),
            new(NotificationChannel.Sms, true, "sms://primary"),
            new(NotificationChannel.DiscordWebhook, true, "discord://hub-notifications"),
            new(NotificationChannel.SlackWebhook, true, "slack://hub-notifications")
        ];
    }

    public int Count => _notifications.Count;

    public long DeliveredCount => Interlocked.Read(ref _delivered);

    public NotificationRecord Send(
        string userId,
        string title,
        string message,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        JsonNode? metadata,
        DateTimeOffset now)
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
            now);

        _notifications.Append(record);
        Interlocked.Increment(ref _delivered);
        return record;
    }

    public IReadOnlyList<NotificationRecord> Recent(string? userId, int limit) =>
        _notifications.Recent(limit, record => userId is null || record.UserId == userId);

    public IReadOnlyList<NotificationEndpoint> Endpoints()
    {
        lock (_endpointGate)
        {
            return [.. _endpoints.OrderBy(endpoint => endpoint.Channel.ToChannelKey(), StringComparer.Ordinal)];
        }
    }

    public void SetEndpoint(NotificationChannel channel, string target, bool enabled)
    {
        lock (_endpointGate)
        {
            var index = _endpoints.FindIndex(endpoint => endpoint.Channel == channel);
            var updated = new NotificationEndpoint(channel, enabled, target);

            if (index >= 0)
            {
                _endpoints[index] = updated;
            }
            else
            {
                _endpoints.Add(updated);
            }
        }
    }
}
