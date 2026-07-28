using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Hub;
using RyzeHub.Application.Platform;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Cli;

internal static class CommandRouter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> ExecuteAsync(string[] args, IServiceProvider provider, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var arguments = new CommandArguments(args[1..]);

        return args[0].ToLowerInvariant() switch
        {
            "run" => await RunAsync(provider, arguments, cancellationToken),
            "health" => await HealthAsync(provider, cancellationToken),
            "validate" => Validate(provider),
            "hub" => await HubAsync(provider, arguments, cancellationToken),
            "deps" => Deps(provider, arguments),
            "docker" => Docker(provider, arguments),
            "encrypt" => Encrypt(provider, arguments),
            "decrypt" => Decrypt(provider, arguments),
            "sign" => Sign(provider, arguments),
            "verify" => Verify(provider, arguments),
            "vault" => await VaultAsync(provider, arguments, cancellationToken),
            "errors" => Errors(provider, arguments),
            "platform" => Platform(provider, arguments),
            "auth" => await AuthAsync(provider, arguments, cancellationToken),
            _ => Unknown(args[0])
        };
    }

    private static async Task<int> RunAsync(IServiceProvider provider, CommandArguments arguments, CancellationToken cancellationToken)
    {
        var pipeline = provider.GetRequiredService<ITicketPipeline>();

        if (arguments.HasFlag("continuous"))
        {
            await pipeline.RunContinuousAsync(cancellationToken);
            return 0;
        }

        var metrics = await pipeline.RunOnceAsync(cancellationToken);
        PrintJson(metrics);

        if (metrics.FailedCount > 0)
        {
            Console.Error.WriteLine($"{metrics.FailedCount} tickets failed!");
            return 1;
        }

        return 0;
    }

    private static async Task<int> HealthAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var health = await provider.GetRequiredService<ITicketPipeline>().HealthCheckAsync(cancellationToken);
        PrintJson(health);
        return health.Status == "unhealthy" ? 1 : 0;
    }

    private static int Validate(IServiceProvider provider)
    {
        var pipelineOptions = provider.GetRequiredService<IOptions<PipelineOptions>>().Value;
        var sourceOptions = provider.GetRequiredService<IOptions<SourceOptions>>().Value;
        var destinationOptions = provider.GetRequiredService<IOptions<DestinationOptions>>().Value;
        var securityOptions = provider.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var authOptions = provider.GetRequiredService<IOptions<RyzeAuthOptions>>().Value;

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(sourceOptions.BaseUrl)) errors.Add("Source:BaseUrl is required");
        if (string.IsNullOrWhiteSpace(sourceOptions.ApiKey)) errors.Add("Source:ApiKey is required");
        if (string.IsNullOrWhiteSpace(destinationOptions.BaseUrl)) errors.Add("Destination:BaseUrl is required");
        if (string.IsNullOrWhiteSpace(destinationOptions.ApiKey)) errors.Add("Destination:ApiKey is required");
        if (string.IsNullOrWhiteSpace(securityOptions.EncryptionKey)) errors.Add("Security:EncryptionKey is required");
        if (string.IsNullOrWhiteSpace(authOptions.Authority)) errors.Add("RyzeAuth:Authority is required");

        PrintJson(new
        {
            valid = errors.Count == 0,
            errors,
            pipeline = new
            {
                pipelineOptions.AutoCategorize,
                pipelineOptions.AutoPriority,
                pipelineOptions.Deduplicate
            },
            ryzeAuth = new
            {
                authOptions.Authority,
                authOptions.ApiBaseUrl,
                authOptions.RequiredApiKeyScope,
                authOptions.ApiKeyIntrospectionEnabled
            },
            hubModules = PlatformModuleCatalog.EnabledModules
        });

        return errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> HubAsync(IServiceProvider provider, CommandArguments arguments, CancellationToken cancellationToken)
    {
        var result = await provider.GetRequiredService<IHubManager>()
            .UpdateHubAsync(arguments.HasFlag("dockerize"), cancellationToken);

        PrintJson(result);
        return result.IsSuccess ? 0 : 1;
    }

    private static int Deps(IServiceProvider provider, CommandArguments arguments)
    {
        var path = arguments.GetValue("path") ?? ".";
        var repos = arguments.GetValue("repos")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [.. HubCatalog.TrackedRepositoryNames()];

        var manager = new DependencyManager(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<DependencyManager>(),
            repos);

        var dependencies = manager.FindInternalDependencies(path);
        PrintJson(new { path, dependencies });
        return 0;
    }

    private static int Docker(IServiceProvider provider, CommandArguments arguments)
    {
        var path = arguments.GetValue("path") ?? ".";
        var manager = new DockerManager(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<DockerManager>(),
            path);

        var language = manager.DetectLanguage(path);
        var success = language == "dotnet"
            ? manager.GenerateDotnetDockerfile(path)
            : manager.GenerateDockerfile(path, language);

        PrintJson(new { path, language, success });
        return success ? 0 : 1;
    }

    private static int Encrypt(IServiceProvider provider, CommandArguments arguments)
    {
        var data = arguments.GetValue("data") ?? throw new ArgumentException("--data is required");
        var engine = provider.GetRequiredService<ICryptoEngine>();
        var envelope = engine.EncryptToEnvelope(data);

        PrintJson(new
        {
            keyId = engine.CurrentKeyId,
            algorithm = "AES-256-GCM",
            originalBytes = Encoding.UTF8.GetByteCount(data),
            encrypted = envelope
        });

        return 0;
    }

    private static int Decrypt(IServiceProvider provider, CommandArguments arguments)
    {
        var data = arguments.GetValue("data") ?? throw new ArgumentException("--data is required");
        var plaintext = provider.GetRequiredService<ICryptoEngine>().DecryptFromEnvelope(data);
        PrintJson(new { decrypted = plaintext });
        return 0;
    }

    private static int Sign(IServiceProvider provider, CommandArguments arguments)
    {
        var data = arguments.GetValue("data") ?? throw new ArgumentException("--data is required");
        PrintJson(provider.GetRequiredService<ISignatureEngine>().Sign(Encoding.UTF8.GetBytes(data)));
        return 0;
    }

    private static int Verify(IServiceProvider provider, CommandArguments arguments)
    {
        var data = arguments.GetValue("data") ?? throw new ArgumentException("--data is required");
        var signature = arguments.GetValue("signature") ?? throw new ArgumentException("--signature is required");

        var valid = provider.GetRequiredService<ISignatureEngine>().Verify(Encoding.UTF8.GetBytes(data), signature);
        PrintJson(new { valid });
        return valid ? 0 : 1;
    }

    private static async Task<int> VaultAsync(IServiceProvider provider, CommandArguments arguments, CancellationToken cancellationToken)
    {
        var vault = provider.GetRequiredService<ISecureVault>();
        var action = arguments.Positional.FirstOrDefault() ?? "list";

        switch (action.ToLowerInvariant())
        {
            case "store":
                await vault.StoreKeyAsync(
                    arguments.GetValue("key-id") ?? throw new ArgumentException("--key-id is required"),
                    Encoding.UTF8.GetBytes(arguments.GetValue("data") ?? throw new ArgumentException("--data is required")),
                    arguments.GetValue("name") ?? "unnamed",
                    "Stored via CLI",
                    [],
                    cancellationToken);
                PrintJson(new { stored = true });
                return 0;

            case "retrieve":
                var keyId = arguments.GetValue("key-id") ?? throw new ArgumentException("--key-id is required");
                var data = await vault.RetrieveKeyAsync(keyId, cancellationToken);
                PrintJson(new { keyId, bytes = data.Length, hex = Convert.ToHexStringLower(data) });
                return 0;

            case "list":
                PrintJson(await vault.ListKeysAsync(cancellationToken));
                return 0;

            case "integrity":
                PrintJson(new { integrityHash = await vault.IntegrityHashAsync(cancellationToken) });
                return 0;

            default:
                Console.Error.WriteLine($"Unknown vault action: {action}");
                return 1;
        }
    }

    private static int Errors(IServiceProvider provider, CommandArguments arguments)
    {
        var engine = provider.GetRequiredService<IErrorDetectionEngine>();
        var action = arguments.Positional.FirstOrDefault() ?? "stats";

        switch (action.ToLowerInvariant())
        {
            case "analyze":
                var message = arguments.GetValue("message") ?? throw new ArgumentException("--message is required");
                PrintJson(engine.DetectError(message, arguments.GetValue("source") ?? "unknown"));
                return 0;

            case "stats":
                PrintJson(engine.GetStatistics());
                return 0;

            case "anomalies":
                PrintJson(engine.DetectAnomalies());
                return 0;

            case "patterns":
                PrintJson(engine.GetPatternStats());
                return 0;

            case "predictions":
                PrintJson(engine.PredictIssues());
                return 0;

            default:
                Console.Error.WriteLine($"Unknown errors action: {action}");
                return 1;
        }
    }

    private static int Platform(IServiceProvider provider, CommandArguments arguments)
    {
        var pipeline = provider.GetRequiredService<ITicketPipeline>();

        if (arguments.GetValue("seed-demo-user") is { } demoUser)
        {
            pipeline.SeedHubDemo(demoUser);
        }

        var hub = pipeline.Hub;
        var action = arguments.Positional.FirstOrDefault() ?? "snapshot";
        var limit = int.TryParse(arguments.GetValue("limit"), out var parsedLimit) ? parsedLimit : 20;
        var userId = arguments.GetValue("user-id");

        switch (action.ToLowerInvariant())
        {
            case "snapshot": PrintJson(hub.Snapshot()); return 0;
            case "health": PrintJson(hub.HealthStatus()); return 0;
            case "modules": PrintJson(hub.ListModuleCatalog()); return 0;
            case "events": PrintJson(hub.ListEvents(limit)); return 0;
            case "endpoints": PrintJson(hub.ListNotificationEndpoints()); return 0;
            case "notifications": PrintJson(hub.ListNotifications(userId, limit)); return 0;
            case "audit": PrintJson(hub.ListAuditLogs(arguments.GetValue("actor-id"), limit)); return 0;
            case "roles": PrintJson(hub.ListRoles()); return 0;
            case "presence": PrintJson(hub.ListPresence(userId)); return 0;
            case "devices": PrintJson(hub.ListDevices(userId)); return 0;
            case "sessions": PrintJson(hub.ListSessions(userId)); return 0;
            case "routes": PrintJson(hub.ListGatewayRoutes()); return 0;
            case "cache": PrintJson(hub.ListCacheEntries()); return 0;
            case "activity": PrintJson(hub.ListActivityFeed(userId, limit)); return 0;
            case "messages": PrintJson(hub.ListMessages(userId, limit)); return 0;
            case "flags": PrintJson(hub.ListFeatureFlags()); return 0;
            case "services": PrintJson(hub.ListServices()); return 0;
            case "security": PrintJson(hub.ListSecurityAlerts(userId, limit)); return 0;
            case "subscriptions": PrintJson(hub.ListSubscriptions()); return 0;
            case "files": PrintJson(hub.ListFileTransfers(arguments.GetValue("owner-id"), limit)); return 0;

            case "access":
                PrintJson(hub.AccessProfile(userId ?? throw new ArgumentException("--user-id is required")));
                return 0;

            case "assign-role":
                hub.AssignRole(
                    userId ?? throw new ArgumentException("--user-id is required"),
                    arguments.GetValue("role") ?? throw new ArgumentException("--role is required"));
                PrintJson(hub.AccessProfile(userId));
                return 0;

            case "grant":
                hub.GrantPermission(
                    userId ?? throw new ArgumentException("--user-id is required"),
                    arguments.GetValue("permission") ?? throw new ArgumentException("--permission is required"));
                PrintJson(hub.AccessProfile(userId));
                return 0;

            case "notify":
                var channels = ParseChannels(arguments.GetValue("channels") ?? "desktop");
                PlatformEnumExtensions.TryParsePriority(arguments.GetValue("priority") ?? "medium", out var priority);
                PrintJson(hub.SendNotification(
                    userId ?? throw new ArgumentException("--user-id is required"),
                    arguments.GetValue("title") ?? "RyzeHub",
                    arguments.GetValue("message") ?? string.Empty,
                    channels,
                    priority,
                    ParseJson(arguments.GetValue("metadata"))));
                return 0;

            case "put-cache":
                var key = arguments.GetValue("key") ?? throw new ArgumentException("--key is required");
                hub.PutCache(key, ParseJson(arguments.GetValue("value")));
                PrintJson(hub.CacheGet(key));
                return 0;

            case "set-flag":
                var flagKey = arguments.GetValue("key") ?? throw new ArgumentException("--key is required");
                hub.SetFeatureFlag(flagKey, arguments.HasFlag("enabled"), arguments.GetValue("description") ?? string.Empty);
                PrintJson(hub.ListFeatureFlags().FirstOrDefault(flag => flag.Key == flagKey));
                return 0;

            case "update-service":
                var service = arguments.GetValue("service") ?? throw new ArgumentException("--service is required");
                hub.UpdateServiceHealth(service, arguments.HasFlag("healthy"),
                    long.TryParse(arguments.GetValue("latency-ms"), out var latency) ? latency : 0);
                PrintJson(hub.ListServices().FirstOrDefault(item => item.Name == service));
                return 0;

            default:
                Console.Error.WriteLine($"Unknown platform action: {action}");
                return 1;
        }
    }

    private static async Task<int> AuthAsync(IServiceProvider provider, CommandArguments arguments, CancellationToken cancellationToken)
    {
        var client = provider.GetRequiredService<IRyzeAuthClient>();
        var action = arguments.Positional.FirstOrDefault() ?? "health";

        switch (action.ToLowerInvariant())
        {
            case "health":
                var (healthy, latency, jwks, grpc) = await client.HealthCheckAsync(cancellationToken);
                PrintJson(new { healthy, latencyMs = (long)latency.TotalMilliseconds, jwksReachable = jwks, grpcReachable = grpc });
                return healthy ? 0 : 1;

            case "introspect-key":
                var apiKey = arguments.GetValue("api-key") ?? throw new ArgumentException("--api-key is required");
                var scope = arguments.GetValue("scope") ?? "hub:tickets:transfer";
                var keyResult = await client.IntrospectApiKeyAsync(apiKey, scope, cancellationToken);
                PrintJson(keyResult);
                return keyResult.Active ? 0 : 1;

            case "introspect-token":
                var token = arguments.GetValue("token") ?? throw new ArgumentException("--token is required");
                var tokenResult = await client.IntrospectTokenAsync(token, cancellationToken);
                PrintJson(tokenResult);
                return tokenResult.Active ? 0 : 1;

            default:
                Console.Error.WriteLine($"Unknown auth action: {action}");
                return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
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

    private static JsonNode? ParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }

    private static void PrintJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void PrintUsage() => Console.WriteLine("""
        RyzeHub CLI

        Usage: ryzehub <command> [action] [--option value] [--flag]

        Commands:
          run [--continuous]                       Run the ticket pipeline
          health                                   Pipeline + RyzeAuth health check
          validate                                 Validate configuration
          hub [--dockerize]                        Clone org, analyse deps, generate Dockerfiles
          deps --path <p> [--repos a,b]            Analyse internal dependencies
          docker --path <p>                        Generate a Dockerfile
          encrypt --data <text>                    AES-256-GCM encrypt
          decrypt --data <envelope>                AES-256-GCM decrypt
          sign --data <text>                       HMAC-SHA256 sign
          verify --data <text> --signature <sig>   Verify a signature
          vault <store|retrieve|list|integrity>    Secure vault operations
          errors <analyze|stats|anomalies|patterns|predictions>
          platform <action> [--user-id ...]        Hub platform modules
          auth <health|introspect-key|introspect-token>
                                                   RyzeAuth control plane

        Examples:
          ryzehub run --continuous
          ryzehub platform snapshot --seed-demo-user user-001
          ryzehub auth introspect-key --api-key rk_live_... --scope hub:tickets:transfer
        """);
}
