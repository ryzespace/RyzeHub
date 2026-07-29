using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics.Detectors;

/// <summary>
/// Compares the latest sample against an exponentially smoothed baseline, which
/// weights recent history more heavily than a plain moving average.
/// </summary>
public sealed class ExponentialSmoothingStrategy(double alpha = 0.3, double threshold = 2.0)
    : IAnomalyDetectionStrategy
{
    private const double Confidence = 0.75;

    public string Method => "exponential_smoothing";

    public AdvancedAnomaly? Detect(string metricName, TimeSeries series)
    {
        var values = series.GetValues();
        if (values.Length == 0)
        {
            return null;
        }

        var smoothed = values[0];
        foreach (var value in values[1..])
        {
            smoothed = alpha * value + (1.0 - alpha) * smoothed;
        }

        if (smoothed == 0.0)
        {
            return null;
        }

        var current = values[^1];
        var deviation = Math.Abs(current - smoothed) / Math.Abs(smoothed);
        if (deviation <= threshold)
        {
            return null;
        }

        return AnomalyFactory.Create(
            metricName,
            Method,
            current,
            smoothed,
            smoothed * (1.0 - threshold),
            smoothed * (1.0 + threshold),
            deviation / (threshold * 2.0),
            Confidence,
            $"Exponential smoothing anomaly: {deviation:F2} dev from smoothed {smoothed:F2}");
    }
}
