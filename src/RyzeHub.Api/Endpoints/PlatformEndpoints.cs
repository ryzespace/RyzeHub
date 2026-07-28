using System.Text.Json.Nodes;
using RyzeHub.Application;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Api.Endpoints;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/platform").WithTags("Hub Platform").RequireAuthorization();

        group.MapGet("/snapshot", (IHubPlatform hub) => Results.Ok(hub.Snapshot()));
        group.MapGet("/health", (IHubPlatform hub) => Results.Ok(hub.HealthStatus())).AllowAnonymous();
        group.MapGet("/modules", (IHubPlatform hub) => Results.Ok(hub.ListModuleCatalog())).AllowAnonymous();

        group.MapGet("/events", (IHubPlatform hub, int limit = 20) => Results.Ok(hub.ListEvents(limit)));
        group.MapGet("/endpoints", (IHubPlatform hub) => Results.Ok(hub.ListNotificationEndpoints()));

        group.MapGet("/notifications", (IHubPlatform hub, string? userId, int limit = 20) =>
            Results.Ok(hub.ListNotifications(userId, limit)));

        group.MapPost("/notifications", (IHubPlatform hub, SendNotificationRequest request) =>
        {
            var channels = ParseChannels(request.Channels);
            if (channels.Count == 0)
            {
                return Results.BadRequest(new { error = "At least one valid notification channel is required." });
            }

            if (!PlatformEnumExtensions.TryParsePriority(request.Priority ?? "medium", out var priority))
            {
                return Results.BadRequest(new { error = $"Unknown notification priority: {request.Priority}" });
            }

            var record = hub.SendNotification(
                request.UserId,
                request.Title,
                request.Message,
                channels,
                priority,
                request.Metadata);

            return Results.Ok(record);
        }).RequireAuthorization("PlatformWrite");

        group.MapGet("/audit", (IHubPlatform hub, string? actorId, int limit = 20) =>
            Results.Ok(hub.ListAuditLogs(actorId, limit)));

        group.MapGet("/roles", (IHubPlatform hub) => Results.Ok(hub.ListRoles()));
        group.MapGet("/access/{userId}", (IHubPlatform hub, string userId) => Results.Ok(hub.AccessProfile(userId)));

        group.MapPost("/access/{userId}/roles/{role}", (IHubPlatform hub, string userId, string role) =>
        {
            hub.AssignRole(userId, role);
            return Results.Ok(hub.AccessProfile(userId));
        }).RequireAuthorization("PlatformAdmin");

        group.MapPost("/access/{userId}/permissions/{permission}", (IHubPlatform hub, string userId, string permission) =>
        {
            hub.GrantPermission(userId, permission);
            return Results.Ok(hub.AccessProfile(userId));
        }).RequireAuthorization("PlatformAdmin");

        group.MapGet("/presence", (IHubPlatform hub, string? userId) => Results.Ok(hub.ListPresence(userId)));
        group.MapGet("/devices", (IHubPlatform hub, string? userId) => Results.Ok(hub.ListDevices(userId)));
        group.MapGet("/sessions", (IHubPlatform hub, string? userId) => Results.Ok(hub.ListSessions(userId)));

        group.MapDelete("/sessions/{sessionId}", (IHubPlatform hub, string sessionId) =>
        {
            hub.TerminateSession(sessionId);
            return Results.NoContent();
        }).RequireAuthorization("PlatformWrite");

        group.MapDelete("/devices/{deviceId}", (IHubPlatform hub, string deviceId) =>
        {
            hub.RevokeDevice(deviceId);
            return Results.NoContent();
        }).RequireAuthorization("PlatformWrite");

        group.MapGet("/routes", (IHubPlatform hub) => Results.Ok(hub.ListGatewayRoutes()));
        group.MapGet("/cache", (IHubPlatform hub, string? key) => key is null
            ? Results.Ok(hub.ListCacheEntries())
            : Results.Ok(hub.CacheGet(key)));

        group.MapPut("/cache/{key}", (IHubPlatform hub, string key, JsonNode? value) =>
        {
            hub.PutCache(key, value);
            return Results.Ok(hub.CacheGet(key));
        }).RequireAuthorization("PlatformWrite");

        group.MapGet("/activity", (IHubPlatform hub, string? userId, int limit = 20) =>
            Results.Ok(hub.ListActivityFeed(userId, limit)));

        group.MapGet("/messages", (IHubPlatform hub, string? userId, int limit = 20) =>
            Results.Ok(hub.ListMessages(userId, limit)));

        group.MapPost("/messages", (IHubPlatform hub, SendMessageRequest request) =>
        {
            hub.RecordInternalMessage(request.FromUser, request.ToUser, request.Body);
            return Results.Ok(hub.ListMessages(request.ToUser, 1));
        }).RequireAuthorization("PlatformWrite");

        group.MapGet("/flags", (IHubPlatform hub) => Results.Ok(hub.ListFeatureFlags()));

        group.MapPut("/flags/{key}", (IHubPlatform hub, string key, SetFeatureFlagRequest request) =>
        {
            hub.SetFeatureFlag(key, request.Enabled, request.Description ?? string.Empty);
            return Results.Ok(hub.ListFeatureFlags().FirstOrDefault(flag => flag.Key == key));
        }).RequireAuthorization("PlatformAdmin");

        group.MapGet("/services", (IHubPlatform hub) => Results.Ok(hub.ListServices()));

        group.MapPut("/services/{service}", (IHubPlatform hub, string service, UpdateServiceRequest request) =>
        {
            hub.UpdateServiceHealth(service, request.Healthy, request.LatencyMs);
            return Results.Ok(hub.ListServices().FirstOrDefault(item => item.Name == service));
        }).RequireAuthorization("PlatformWrite");

        group.MapGet("/security", (IHubPlatform hub, string? userId, int limit = 20) =>
            Results.Ok(hub.ListSecurityAlerts(userId, limit)));

        group.MapGet("/subscriptions", (IHubPlatform hub) => Results.Ok(hub.ListSubscriptions()));

        group.MapGet("/files", (IHubPlatform hub, string? ownerId, int limit = 20) =>
            Results.Ok(hub.ListFileTransfers(ownerId, limit)));

        return endpoints;
    }

    private static List<NotificationChannel> ParseChannels(string? value)
    {
        var channels = new List<NotificationChannel>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return channels;
        }

        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (PlatformEnumExtensions.TryParseChannel(raw, out var channel) && !channels.Contains(channel))
            {
                channels.Add(channel);
            }
        }

        return channels;
    }

    public sealed record SendNotificationRequest(
        string UserId,
        string Title,
        string Message,
        string? Channels,
        string? Priority,
        JsonNode? Metadata);

    public sealed record SendMessageRequest(string FromUser, string ToUser, string Body);

    public sealed record SetFeatureFlagRequest(bool Enabled, string? Description);

    public sealed record UpdateServiceRequest(bool Healthy, long LatencyMs);
}
