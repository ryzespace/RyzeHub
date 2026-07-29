using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics;

/// <summary>
/// Z-score scan over recorded metric samples, used by the error detection engine for
/// simple threshold alerting. The richer multi-strategy analysis lives in
/// <see cref="AnomalyDetector"/>.
/// </summary>
internal sealed class MetricAnomalyScanner(ILogger logger, int maxHistorySize = 1_000)
{
    private const int MinimumSamples = 10;
    private const double DefaultThreshold = 2.0;

    private readonly ConcurrentDictionary<string, TimeSeries> _history = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> _thresholds = new(StringComparer.Ordinal);

    public void SetThreshold(string metricName, double threshold) => _thresholds[metricName] = threshold;

    public void Record(string metricName, double value) =>
        _history
            .GetOrAdd(metricName, name => new TimeSeries(name, maxHistorySize))
            .AddPoint(DateTimeOffset.UtcNow, value);

    public IReadOnlyList<AnomalyResult> Scan()
    {
        var anomalies = new List<AnomalyResult>();

        foreach (var (metricName, series) in _history)
        {
            if (series.Count < MinimumSamples)
            {
                continue;
            }

            var values = series.GetValues();
            var current = values[^1];
            var mean = values.Average();

            // Population standard deviation: this scan reports on the observed window itself.
            var standardDeviation = Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / values.Length);
            var threshold = _thresholds.GetValueOrDefault(metricName, DefaultThreshold);
            var deviation = standardDeviation > 0 ? Math.Abs(current - mean) / standardDeviation : 0.0;

            if (deviation <= threshold)
            {
                continue;
            }

            anomalies.Add(new AnomalyResult(
                DateTimeOffset.UtcNow,
                metricName,
                current,
                threshold,
                deviation,
                Classify(deviation, threshold),
                $"Metric {metricName} is {deviation:F2} std devs from mean (current: {current:F2}, mean: {mean:F2})"));

            logger.LogWarning("Anomaly detected: {MetricName}", metricName);
        }

        return anomalies;
    }

    private static ErrorSeverity Classify(double deviation, double threshold) => deviation switch
    {
        _ when deviation > threshold * 2.0 => ErrorSeverity.Critical,
        _ when deviation > threshold * 1.5 => ErrorSeverity.High,
        _ => ErrorSeverity.Medium
    };
}
