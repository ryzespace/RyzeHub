using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics;

/// <summary>The error signatures RyzeHub recognises out of the box.</summary>
public static class DefaultErrorPatterns
{
    public static IReadOnlyList<ErrorPattern> All { get; } =
    [
        new()
        {
            Id = "network_timeout",
            Name = "Network Timeout",
            Description = "Connection or request timeout",
            RegexPattern = "(?i)(timeout|timed out|deadline exceeded)",
            Category = ErrorCategory.Timeout,
            Severity = ErrorSeverity.Medium,
            AutoResolve = false
        },
        new()
        {
            Id = "auth_failure",
            Name = "Authentication Failure",
            Description = "Authentication or authorization error",
            RegexPattern = "(?i)(unauthorized|forbidden|auth.*fail|invalid.*token|401|403)",
            Category = ErrorCategory.Authentication,
            Severity = ErrorSeverity.High,
            AutoResolve = false
        },
        new()
        {
            Id = "rate_limit",
            Name = "Rate Limit Exceeded",
            Description = "API rate limit exceeded",
            RegexPattern = "(?i)(rate.?limit|too many requests|429|throttl)",
            Category = ErrorCategory.RateLimit,
            Severity = ErrorSeverity.Medium,
            AutoResolve = true
        },
        new()
        {
            Id = "validation_error",
            Name = "Validation Error",
            Description = "Data validation failed",
            RegexPattern = "(?i)(validation.*fail|invalid.*data|required.*field|missing.*field)",
            Category = ErrorCategory.Validation,
            Severity = ErrorSeverity.Low,
            AutoResolve = false
        },
        new()
        {
            Id = "connection_error",
            Name = "Connection Error",
            Description = "Network connection failure",
            RegexPattern = "(?i)(connection.*refused|connection.*reset|network.*unreachable|503)",
            Category = ErrorCategory.Network,
            Severity = ErrorSeverity.High,
            AutoResolve = false
        },
        new()
        {
            Id = "ryzeauth_denied",
            Name = "RyzeAuth Denied",
            Description = "RyzeAuth rejected an API key or token",
            RegexPattern = "(?i)(api key (revoked|expired|inactive)|missing scope|introspection failed|organization disabled)",
            Category = ErrorCategory.Authorization,
            Severity = ErrorSeverity.Critical,
            AutoResolve = false
        }
    ];
}
