using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics.Detectors;

/// <summary>
/// One statistical test over a metric window. Strategies are stateless so they can be
/// shared across metrics and unit tested against a hand-built <see cref="TimeSeries"/>.
/// </summary>
public interface IAnomalyDetectionStrategy
{
    /// <summary>Identifier recorded in the anomaly metadata, e.g. <c>z_score</c>.</summary>
    string Method { get; }

    /// <summary>Returns an anomaly when the latest sample fails this test, otherwise null.</summary>
    AdvancedAnomaly? Detect(string metricName, TimeSeries series);
}

internal static class AnomalyFactory
{
    public static AdvancedAnomaly Create(
        string metricName,
        string method,
        double current,
        double reference,
        double lowerBound,
        double upperBound,
        double severity,
        double confidence,
        string description) =>
        new(
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            metricName,
            current > reference ? AnomalyType.Spike : AnomalyType.Drop,
            Math.Clamp(severity, 0.0, 1.0),
            Math.Clamp(confidence, 0.0, 1.0),
            current,
            lowerBound,
            upperBound,
            description,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = method });
}
