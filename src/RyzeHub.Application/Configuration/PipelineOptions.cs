using System.ComponentModel.DataAnnotations;

namespace RyzeHub.Application.Configuration;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    public bool AutoCategorize { get; set; } = true;
    public bool AutoPriority { get; set; } = true;
    public bool FilterResolved { get; set; } = true;
    public bool FilterClosed { get; set; } = true;
    public bool Deduplicate { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
    public int PollIntervalSeconds { get; set; } = 300;
}

public sealed class SourceOptions
{
    public const string SectionName = "Source";

    [Required]
    public string BaseUrl { get; set; } = "https://client-dashboard.example.com/api/v1";

    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 50;
    public int RateLimit { get; set; } = 100;
}

public sealed class DestinationOptions
{
    public const string SectionName = "Destination";

    [Required]
    public string BaseUrl { get; set; } = "https://helpcenter.example.com/api/v1";

    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
    public int RateLimit { get; set; } = 100;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerRecoverySeconds { get; set; } = 60;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Base64 encoded 256-bit key used for payload encryption at rest and in transit.</summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>Base64 encoded 256-bit key used for HMAC-SHA256 request and ticket signatures.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public bool AuditLogEnabled { get; set; } = true;
    public bool ChecksumEnabled { get; set; } = true;
    public bool SensitiveFieldsMask { get; set; } = true;
    public string VaultPath { get; set; } = "vault.json";
    public string VaultUser { get; set; } = "system";
    public int KeyRotationDays { get; set; } = 30;
}

public sealed class HubPlatformOptions
{
    public const string SectionName = "Hub";

    public long EventRetentionSeconds { get; set; } = 86_400;
    public string CacheProvider { get; set; } = "redis";
    public long CacheTtlSeconds { get; set; } = 3_600;
    public string GatewayBasePath { get; set; } = "/api";
    public int GatewayRateLimit { get; set; } = 120;
    public bool TelemetryEnabled { get; set; } = true;
    public long NotificationRetentionSeconds { get; set; } = 604_800;
    public int MaxEvents { get; set; } = 10_000;
    public int MaxNotifications { get; set; } = 10_000;
}

/// <summary>
/// Configuration for the RyzeAuth control plane that RyzeHub authenticates against.
/// </summary>
public sealed class RyzeAuthOptions
{
    public const string SectionName = "RyzeAuth";

    /// <summary>Keycloak realm authority, e.g. https://auth.ryzespace.example/realms/ryzespace.</summary>
    public string Authority { get; set; } = "http://localhost:8080/realms/ryzespace";

    /// <summary>Audience expected in RyzeAuth issued access tokens.</summary>
    public string ValidAudience { get; set; } = "ryzehub-api";

    /// <summary>Base address of the RyzeAuth ASP.NET Core API (REST + gRPC).</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:8081";

    /// <summary>Client credentials used by RyzeHub to call RyzeAuth internal endpoints.</summary>
    public string ClientId { get; set; } = "ryzehub-service";

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Scope required on an API key before RyzeHub accepts a pipeline request.</summary>
    public string RequiredApiKeyScope { get; set; } = "hub:tickets:transfer";

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Enables the gRPC API-key introspection channel against RyzeAuth.</summary>
    public bool ApiKeyIntrospectionEnabled { get; set; } = true;

    /// <summary>Cache lifetime for positive introspection results.</summary>
    public int IntrospectionCacheSeconds { get; set; } = 60;

    /// <summary>Mirror RyzeHub audit entries into the RyzeAuth security audit trail.</summary>
    public bool ForwardAuditEvents { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Maps RyzeAuth/Keycloak realm roles onto RyzeHub platform roles.</summary>
    public Dictionary<string, string> RoleMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ryzehub-user"] = "User",
        ["ryzehub-seller"] = "Seller",
        ["ryzehub-moderator"] = "Moderator",
        ["ryzehub-support"] = "Support",
        ["ryzehub-admin"] = "Admin",
        ["ryzehub-superadmin"] = "SuperAdmin"
    };
}

public sealed class HubManagerOptions
{
    public const string SectionName = "HubManager";

    public string OrganizationName { get; set; } = "ryzespace";
    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://api.github.com";
    public string HubDirectory { get; set; } = "github_hub";
}
