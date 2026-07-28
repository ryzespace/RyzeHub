using Microsoft.Extensions.Configuration;

namespace RyzeHub.Cli;

/// <summary>
/// Maps the historical flat environment variables onto the structured configuration sections,
/// so existing deployments and workflows keep working after the C# rewrite.
/// </summary>
public static class EnvironmentConfiguration
{
    private static readonly (string Variable, string ConfigurationKey)[] Mappings =
    [
        ("CLIENT_DASHBOARD_URL", "Source:BaseUrl"),
        ("CLIENT_DASHBOARD_API_KEY", "Source:ApiKey"),
        ("SOURCE_TIMEOUT", "Source:TimeoutSeconds"),
        ("SOURCE_MAX_RETRIES", "Source:MaxRetries"),
        ("SOURCE_BATCH_SIZE", "Source:BatchSize"),
        ("SOURCE_RATE_LIMIT", "Source:RateLimit"),

        ("HELPCENTER_URL", "Destination:BaseUrl"),
        ("HELPCENTER_API_KEY", "Destination:ApiKey"),
        ("DEST_TIMEOUT", "Destination:TimeoutSeconds"),
        ("DEST_MAX_RETRIES", "Destination:MaxRetries"),
        ("DEST_RATE_LIMIT", "Destination:RateLimit"),

        ("ENCRYPTION_KEY", "Security:EncryptionKey"),
        ("SIGNING_KEY", "Security:SigningKey"),
        ("AUDIT_LOG_ENABLED", "Security:AuditLogEnabled"),
        ("CHECKSUM_ENABLED", "Security:ChecksumEnabled"),
        ("VAULT_PATH", "Security:VaultPath"),
        ("VAULT_USER", "Security:VaultUser"),

        ("PIPELINE_LOG_LEVEL", "Pipeline:LogLevel"),
        ("POLL_INTERVAL", "Pipeline:PollIntervalSeconds"),

        ("HUB_EVENT_RETENTION_SECONDS", "Hub:EventRetentionSeconds"),
        ("HUB_CACHE_PROVIDER", "Hub:CacheProvider"),
        ("HUB_CACHE_TTL_SECONDS", "Hub:CacheTtlSeconds"),
        ("HUB_GATEWAY_BASE_PATH", "Hub:GatewayBasePath"),
        ("HUB_GATEWAY_RATE_LIMIT", "Hub:GatewayRateLimit"),
        ("HUB_TELEMETRY_ENABLED", "Hub:TelemetryEnabled"),
        ("HUB_NOTIFICATION_RETENTION_SECONDS", "Hub:NotificationRetentionSeconds"),

        ("HUB_ORG_NAME", "HubManager:OrganizationName"),
        ("GITHUB_TOKEN", "HubManager:Token"),
        ("HUB_DIR", "HubManager:HubDirectory"),

        ("RYZEAUTH_AUTHORITY", "RyzeAuth:Authority"),
        ("RYZEAUTH_AUDIENCE", "RyzeAuth:ValidAudience"),
        ("RYZEAUTH_API_BASE_URL", "RyzeAuth:ApiBaseUrl"),
        ("RYZEAUTH_CLIENT_ID", "RyzeAuth:ClientId"),
        ("RYZEAUTH_CLIENT_SECRET", "RyzeAuth:ClientSecret"),
        ("RYZEAUTH_REQUIRED_SCOPE", "RyzeAuth:RequiredApiKeyScope"),
        ("RYZEAUTH_API_KEY_INTROSPECTION", "RyzeAuth:ApiKeyIntrospectionEnabled"),
        ("RYZEAUTH_FORWARD_AUDIT", "RyzeAuth:ForwardAuditEvents")
    ];

    public static IConfigurationBuilder AddRyzeHubEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (variable, configurationKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[configurationKey] = value;
            }
        }

        return values.Count == 0 ? builder : builder.AddInMemoryCollection(values);
    }

    /// <summary>Loads a dotenv style file so local runs mirror the docker-compose environment.</summary>
    public static IConfigurationBuilder AddDotEnvFile(this IConfigurationBuilder builder, string path = ".env")
    {
        if (!File.Exists(path))
        {
            return builder;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        return builder;
    }
}
