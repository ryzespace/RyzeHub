using System.Text.Json.Nodes;
using RyzeHub.Application.Platform.Stores;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform;

public interface IPlatformEventPublisher
{
    /// <summary>
    /// Records an event on the bus and fans a notification out to each recipient,
    /// updating telemetry along the way.
    /// </summary>
    void Publish(
        RealtimeEventType eventType,
        string actorId,
        string subjectId,
        IReadOnlyList<string> recipients,
        JsonNode? payload,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        string notificationMessage);
}

public sealed class PlatformEventPublisher(
    IRealtimeEventStore events,
    INotificationStore notifications,
    IOperationsStore operations,
    ISystemClock clock) : IPlatformEventPublisher
{
    public void Publish(
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
        var now = clock.UtcNow;

        events.Append(new RealtimeEvent(
            Guid.NewGuid().ToString(),
            topic,
            eventType,
            actorId,
            subjectId,
            recipients,
            payload?.DeepClone(),
            now));

        operations.RecordEmittedEvent();
        operations.RecordApiRequest(failed: false);

        foreach (var recipient in recipients)
        {
            notifications.Send(
                recipient,
                topic,
                notificationMessage,
                channels,
                priority,
                payload?.DeepClone(),
                now);
        }
    }
}
