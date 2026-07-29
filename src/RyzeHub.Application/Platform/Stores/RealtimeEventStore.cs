using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IRealtimeEventStore
{
    void Append(RealtimeEvent realtimeEvent);

    IReadOnlyList<RealtimeEvent> Recent(int limit);

    int Count { get; }

    IReadOnlyList<EventSubscription> Subscriptions { get; }
}

/// <summary>Real Time Event System + Event Bus: rolling event log and static topic subscriptions.</summary>
public sealed class RealtimeEventStore : IRealtimeEventStore
{
    private readonly BoundedLog<RealtimeEvent> _events;

    public RealtimeEventStore(IOptions<HubPlatformOptions> options)
    {
        _events = new BoundedLog<RealtimeEvent>(options.Value.MaxEvents);
    }

    public int Count => _events.Count;

    public void Append(RealtimeEvent realtimeEvent) => _events.Append(realtimeEvent);

    public IReadOnlyList<RealtimeEvent> Recent(int limit) => _events.Recent(limit);

    public IReadOnlyList<EventSubscription> Subscriptions { get; } =
    [
        new("payment.completed", ["billing_service", "notification_center", "admin_dashboard", "user_dashboard"]),
        new("support.status_updated", ["helpcenter", "client_dashboard", "notification_center"]),
        new("security.warning", ["security_center", "notification_center", "audit_log_engine", "ryzeauth"]),
        new("server.activated", ["infrastructure_service", "notification_center", "activity_feed"]),
        new("security.login_recorded", ["security_center", "ryzeauth", "activity_feed"])
    ];
}
