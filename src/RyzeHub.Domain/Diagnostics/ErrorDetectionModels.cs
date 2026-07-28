using System.Text.Json.Serialization;

namespace RyzeHub.Domain.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<ErrorSeverity>))]
public enum ErrorSeverity
{
    Low,
    Medium,
    High,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter<ErrorCategory>))]
public enum ErrorCategory
{
    Network,
    Authentication,
    Authorization,
    Validation,
    Serialization,
    Database,
    External,
    Configuration,
    Timeout,
    RateLimit,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter<AnomalyType>))]
public enum AnomalyType
{
    Spike,
    Drop,
    Trend,
    Seasonality,
    Outlier
}

public sealed record DetectedError(
    string Id,
    DateTimeOffset Timestamp,
    string Message,
    ErrorCategory Category,
    ErrorSeverity Severity,
    string Source,
    IReadOnlyDictionary<string, string> Context,
    string? PatternId,
    string? CorrelationId);

public sealed record ErrorPattern
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RegexPattern { get; init; }
    public required ErrorCategory Category { get; init; }
    public required ErrorSeverity Severity { get; init; }
    public long OccurrenceCount { get; init; }
    public DateTimeOffset FirstSeen { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.UtcNow;
    public bool AutoResolve { get; init; }
}

public sealed record AnomalyResult(
    DateTimeOffset Timestamp,
    string MetricName,
    double CurrentValue,
    double Threshold,
    double Deviation,
    ErrorSeverity Severity,
    string Description);

public sealed record AdvancedAnomaly(
    string Id,
    DateTimeOffset Timestamp,
    string MetricName,
    AnomalyType AnomalyType,
    double Severity,
    double Confidence,
    double Value,
    double ExpectedLowerBound,
    double ExpectedUpperBound,
    string Description,
    IReadOnlyDictionary<string, string> Metadata);
