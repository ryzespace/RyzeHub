using Microsoft.Extensions.DependencyInjection;
using RyzeHub.Application;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Cli.Commands;

internal sealed class PlatformCommand : ICommandHandler
{
    public string Name => "platform";

    public string Usage => "platform <action> [--user-id ...] [--seed-demo-user <id>]";

    public string Description => "Inspect and drive the hub platform modules";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var pipeline = provider.GetRequiredService<ITicketPipeline>();

        if (arguments.GetValue("seed-demo-user") is { } demoUser)
        {
            pipeline.SeedHubDemo(demoUser);
        }

        var hub = pipeline.Hub;
        var action = arguments.Action("snapshot");
        var limit = arguments.GetInt32("limit", 20);
        var userId = arguments.GetValue("user-id");

        return Task.FromResult(Execute(hub, action, userId, limit, arguments));
    }

    private static int Execute(IHubPlatform hub, string action, string? userId, int limit, CommandArguments arguments) =>
        action switch
        {
            "snapshot" => Write(hub.Snapshot()),
            "health" => Write(hub.HealthStatus()),
            "modules" => Write(hub.ListModuleCatalog()),
            "events" => Write(hub.ListEvents(limit)),
            "endpoints" => Write(hub.ListNotificationEndpoints()),
            "notifications" => Write(hub.ListNotifications(userId, limit)),
            "audit" => Write(hub.ListAuditLogs(arguments.GetValue("actor-id"), limit)),
            "roles" => Write(hub.ListRoles()),
            "presence" => Write(hub.ListPresence(userId)),
            "devices" => Write(hub.ListDevices(userId)),
            "sessions" => Write(hub.ListSessions(userId)),
            "routes" => Write(hub.ListGatewayRoutes()),
            "cache" => Write(hub.ListCacheEntries()),
            "activity" => Write(hub.ListActivityFeed(userId, limit)),
            "messages" => Write(hub.ListMessages(userId, limit)),
            "flags" => Write(hub.ListFeatureFlags()),
            "services" => Write(hub.ListServices()),
            "security" => Write(hub.ListSecurityAlerts(userId, limit)),
            "subscriptions" => Write(hub.ListSubscriptions()),
            "files" => Write(hub.ListFileTransfers(arguments.GetValue("owner-id"), limit)),
            "access" => Write(hub.AccessProfile(RequireUser(userId))),
            "assign-role" => AssignRole(hub, RequireUser(userId), arguments),
            "grant" => Grant(hub, RequireUser(userId), arguments),
            "notify" => Notify(hub, RequireUser(userId), arguments),
            "put-cache" => PutCache(hub, arguments),
            "set-flag" => SetFlag(hub, arguments),
            "update-service" => UpdateService(hub, arguments),
            _ => UnknownAction(action)
        };

    private static int AssignRole(IHubPlatform hub, string userId, CommandArguments arguments)
    {
        hub.AssignRole(userId, arguments.RequireValue("role"));
        return Write(hub.AccessProfile(userId));
    }

    private static int Grant(IHubPlatform hub, string userId, CommandArguments arguments)
    {
        hub.GrantPermission(userId, arguments.RequireValue("permission"));
        return Write(hub.AccessProfile(userId));
    }

    private static int Notify(IHubPlatform hub, string userId, CommandArguments arguments)
    {
        var channels = ParseChannels(arguments.GetValue("channels") ?? "desktop");
        PlatformEnumExtensions.TryParsePriority(arguments.GetValue("priority") ?? "medium", out var priority);

        return Write(hub.SendNotification(
            userId,
            arguments.GetValue("title") ?? "RyzeHub",
            arguments.GetValue("message") ?? string.Empty,
            channels,
            priority,
            ConsoleOutput.ParseJson(arguments.GetValue("metadata"))));
    }

    private static int PutCache(IHubPlatform hub, CommandArguments arguments)
    {
        var key = arguments.RequireValue("key");
        hub.PutCache(key, ConsoleOutput.ParseJson(arguments.GetValue("value")));
        return Write(hub.CacheGet(key));
    }

    private static int SetFlag(IHubPlatform hub, CommandArguments arguments)
    {
        var key = arguments.RequireValue("key");
        hub.SetFeatureFlag(key, arguments.HasFlag("enabled"), arguments.GetValue("description") ?? string.Empty);
        return Write(hub.ListFeatureFlags().FirstOrDefault(flag => flag.Key == key));
    }

    private static int UpdateService(IHubPlatform hub, CommandArguments arguments)
    {
        var service = arguments.RequireValue("service");
        hub.UpdateServiceHealth(service, arguments.HasFlag("healthy"), arguments.GetInt64("latency-ms", 0));
        return Write(hub.ListServices().FirstOrDefault(item => item.Name == service));
    }

    private static List<NotificationChannel> ParseChannels(string value)
    {
        var channels = new List<NotificationChannel>();

        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (PlatformEnumExtensions.TryParseChannel(raw, out var channel) && !channels.Contains(channel))
            {
                channels.Add(channel);
            }
        }

        return channels.Count == 0 ? [NotificationChannel.Desktop] : channels;
    }

    private static string RequireUser(string? userId) =>
        userId ?? throw new CommandUsageException("--user-id is required");

    private static int Write<T>(T value)
    {
        ConsoleOutput.WriteJson(value);
        return ExitCodes.Success;
    }

    private static int UnknownAction(string action)
    {
        ConsoleOutput.WriteError($"Unknown platform action: {action}");
        return ExitCodes.Failure;
    }
}
