using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics.Detectors;

/// <summary>
/// Interquartile range test: flags samples outside Q1/Q3 widened by
/// <paramref name="multiplier"/> IQRs. Robust against a few extreme values.
/// </summary>
public sealed class IqrStrategy(double multiplier = 1.5) : IAnomalyDetectionStrategy
{
    private const double Confidence = 0.8;

    public string Method => "iqr";

    public AdvancedAnomaly? Detect(string metricName, TimeSeries series)
    {
        var percentiles = series.Percentiles(25.0, 75.0);
        if (!percentiles.TryGetValue(25.0, out var q1) || !percentiles.TryGetValue(75.0, out var q3))
        {
            return null;
        }

        var iqr = q3 - q1;
        if (iqr == 0.0)
        {
            return null;
        }

        var values = series.GetValues();
        if (values.Length == 0)
        {
            return null;
        }

        var current = values[^1];
        var lowerBound = q1 - multiplier * iqr;
        var upperBound = q3 + multiplier * iqr;

        if (current >= lowerBound && current <= upperBound)
        {
            return null;
        }

        var deviation = current < lowerBound
            ? (lowerBound - current) / iqr
            : (current - upperBound) / iqr;

        // Compare against the midpoint so spikes and drops classify correctly.
        return AnomalyFactory.Create(
            metricName,
            Method,
            current,
            (lowerBound + upperBound) / 2.0,
            lowerBound,
            upperBound,
            deviation / (multiplier * 2.0),
            Confidence,
            $"IQR anomaly: value {current:F2} outside [{lowerBound:F2}, {upperBound:F2}]");
    }
}
