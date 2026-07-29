using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics.Detectors;

/// <summary>
/// Flags samples whose relative deviation from the trailing mean of
/// <paramref name="window"/> points exceeds <paramref name="threshold"/>.
/// </summary>
public sealed class MovingAverageStrategy(int window = 10, double threshold = 2.0) : IAnomalyDetectionStrategy
{
    private const double Confidence = 0.7;

    public string Method => "moving_average";

    public AdvancedAnomaly? Detect(string metricName, TimeSeries series)
    {
        var values = series.GetValues();
        if (values.Length < window)
        {
            return null;
        }

        var current = values[^1];
        var movingAverage = values[^window..].Average();

        // Relative deviation is undefined against a zero baseline.
        if (movingAverage == 0.0)
        {
            return null;
        }

        var deviation = Math.Abs(current - movingAverage) / Math.Abs(movingAverage);
        if (deviation <= threshold)
        {
            return null;
        }

        return AnomalyFactory.Create(
            metricName,
            Method,
            current,
            movingAverage,
            movingAverage * (1.0 - threshold),
            movingAverage * (1.0 + threshold),
            deviation / (threshold * 2.0),
            Confidence,
            $"Moving average anomaly: {deviation:F2} dev from MA {movingAverage:F2}");
    }
}
