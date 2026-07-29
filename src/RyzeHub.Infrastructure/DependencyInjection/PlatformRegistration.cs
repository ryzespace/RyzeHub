using Microsoft.Extensions.DependencyInjection;
using RyzeHub.Application;
using RyzeHub.Application.Platform;
using RyzeHub.Application.Platform.Stores;

namespace RyzeHub.Infrastructure.DependencyInjection;

/// <summary>Registers the hub platform facade and the per-module stores behind it.</summary>
internal static class PlatformRegistration
{
    public static IServiceCollection AddHubPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IRealtimeEventStore, RealtimeEventStore>();
        services.AddSingleton<INotificationStore, NotificationStore>();
        services.AddSingleton<IAuditStore, AuditStore>();
        services.AddSingleton<IAccessControlStore, AccessControlStore>();
        services.AddSingleton<IIdentityStateStore, IdentityStateStore>();
        services.AddSingleton<IGatewayCacheStore, GatewayCacheStore>();
        services.AddSingleton<IEngagementStore, EngagementStore>();
        services.AddSingleton<IOperationsStore, OperationsStore>();
        services.AddSingleton<IPlatformEventPublisher, PlatformEventPublisher>();
        services.AddSingleton<IHubPlatform, HubPlatform>();

        return services;
    }
}
