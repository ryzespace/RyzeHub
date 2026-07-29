using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics.Detectors;

/// <summary>Flags samples more than <paramref name="threshold"/> standard deviations from the mean.</summary>
public sealed class ZScoreStrategy(double threshold = 3.0) : IAnomalyDetectionStrategy
{
    public string Method => "z_score";

    public AdvancedAnomaly? Detect(string metricName, TimeSeries series)
    {
        var values = series.GetValues();
        if (values.Length == 0)
        {
            return null;
        }

        var current = values[^1];
        var mean = series.Mean();
        var standardDeviation = series.StandardDeviation();

        // A flat series has no spread, so no sample can be an outlier.
        if (standardDeviation == 0.0)
        {
            return null;
        }

        var zScore = Math.Abs(current - mean) / standardDeviation;
        if (zScore <= threshold)
        {
            return null;
        }

        return AnomalyFactory.Create(
            metricName,
            Method,
            current,
            mean,
            mean - threshold * standardDeviation,
            mean + threshold * standardDeviation,
            zScore / (threshold * 2.0),
            zScore / threshold,
            $"Z-score anomaly: {zScore:F2} (threshold: {threshold:F2})");
    }
}
