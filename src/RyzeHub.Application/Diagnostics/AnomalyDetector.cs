using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics;

/// <summary>
/// Rolling window of samples for a single metric.
/// </summary>
public sealed class TimeSeries(string name, int maxSize)
{
    private readonly Queue<(DateTimeOffset Timestamp, double Value)> _data = new();

    public string Name { get; } = name;

    public int Count
    {
        get
        {
            lock (_data)
            {
                return _data.Count;
            }
        }
    }

    public void AddPoint(DateTimeOffset timestamp, double value)
    {
        lock (_data)
        {
            _data.Enqueue((timestamp, value));
            while (_data.Count > maxSize)
            {
                _data.Dequeue();
            }
        }
    }

    public double[] GetValues()
    {
        lock (_data)
        {
            return [.. _data.Select(static point => point.Value)];
        }
    }

    public double Mean()
    {
        var values = GetValues();
        return values.Length == 0 ? 0.0 : values.Average();
    }

    public double StandardDeviation()
    {
        var values = GetValues();
        if (values.Length < 2)
        {
            return 0.0;
        }

        var mean = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Length - 1));
    }

    public IReadOnlyDictionary<double, double> Percentiles(params double[] percentiles)
    {
        var sorted = GetValues();
        Array.Sort(sorted);

        var result = new Dictionary<double, double>();
        if (sorted.Length == 0)
        {
            return result;
        }

        foreach (var percentile in percentiles)
        {
            var index = (int)Math.Round(percentile / 100.0 * (sorted.Length - 1), MidpointRounding.AwayFromZero);
            result[percentile] = sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }

        return result;
    }
}

internal abstract record DetectionMethod
{
    internal sealed record ZScore(double Threshold) : DetectionMethod;

    internal sealed record Iqr(double Multiplier) : DetectionMethod;

    internal sealed record MovingAverage(int Window, double Threshold) : DetectionMethod;

    internal sealed record ExponentialSmoothing(double Alpha, double Threshold) : DetectionMethod;
}

/// <summary>
/// Statistical anomaly detection using Z-score, IQR, moving average and exponential smoothing.
/// </summary>
public sealed class AnomalyDetector : IAnomalyDetector
{
    private const int MaxAlerts = 10_000;

    private readonly ConcurrentDictionary<string, TimeSeries> _timeSeries = new(StringComparer.Ordinal);
    private readonly List<AdvancedAnomaly> _alerts = [];
    private readonly Lock _alertGate = new();
    private readonly ILogger<AnomalyDetector> _logger;

    private readonly DetectionMethod[] _detectionMethods =
    [
        new DetectionMethod.ZScore(3.0),
        new DetectionMethod.Iqr(1.5),
        new DetectionMethod.MovingAverage(10, 2.0)
    ];

    public AnomalyDetector(ILogger<AnomalyDetector> logger)
    {
        _logger = logger;
        _logger.LogInformation(
            "Initialized advanced anomaly detector with {Count} methods",
            _detectionMethods.Length);
    }

    public void TrackMetric(string metricName, int maxHistory) =>
        _timeSeries[metricName] = new TimeSeries(metricName, maxHistory);

    public void Record(string metricName, double value)
    {
        var series = _timeSeries.GetOrAdd(metricName, static name => new TimeSeries(name, 1_000));
        series.AddPoint(DateTimeOffset.UtcNow, value);
    }

    public IReadOnlyList<AdvancedAnomaly> Detect()
    {
        var anomalies = new List<AdvancedAnomaly>();

        foreach (var (metricName, series) in _timeSeries)
        {
            if (series.Count < 10)
            {
                continue;
            }

            foreach (var method in _detectionMethods)
            {
                var anomaly = DetectWithMethod(metricName, series, method);
                if (anomaly is not null)
                {
                    anomalies.Add(anomaly);
                }
            }
        }

        if (anomalies.Count > 0)
        {
            lock (_alertGate)
            {
                _alerts.AddRange(anomalies);
                if (_alerts.Count > MaxAlerts)
                {
                    _alerts.RemoveRange(0, _alerts.Count - MaxAlerts);
                }
            }

            _logger.LogWarning("Detected {Count} anomalies", anomalies.Count);
        }

        return anomalies;
    }

    private static AdvancedAnomaly? DetectWithMethod(string metricName, TimeSeries series, DetectionMethod method) =>
        method switch
        {
            DetectionMethod.ZScore zScore => ZScoreDetection(metricName, series, zScore.Threshold),
            DetectionMethod.Iqr iqr => IqrDetection(metricName, series, iqr.Multiplier),
            DetectionMethod.MovingAverage movingAverage =>
                MovingAverageDetection(metricName, series, movingAverage.Window, movingAverage.Threshold),
            DetectionMethod.ExponentialSmoothing smoothing =>
                ExponentialSmoothingDetection(metricName, series, smoothing.Alpha, smoothing.Threshold),
            _ => null
        };

    private static AdvancedAnomaly? ZScoreDetection(string metricName, TimeSeries series, double threshold)
    {
        var values = series.GetValues();
        if (values.Length == 0)
        {
            return null;
        }

        var current = values[^1];
        var mean = series.Mean();
        var stdDev = series.StandardDeviation();

        if (stdDev == 0.0)
        {
            return null;
        }

        var zScore = Math.Abs(current - mean) / stdDev;
        if (zScore <= threshold)
        {
            return null;
        }

        return new AdvancedAnomaly(
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            metricName,
            current > mean ? AnomalyType.Spike : AnomalyType.Drop,
            Math.Min(zScore / (threshold * 2.0), 1.0),
            Math.Min(zScore / threshold, 1.0),
            current,
            mean - threshold * stdDev,
            mean + threshold * stdDev,
            $"Z-score anomaly: {zScore:F2} (threshold: {threshold:F2})",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "z_score" });
    }

    private static AdvancedAnomaly? IqrDetection(string metricName, TimeSeries series, double multiplier)
    {
        var percentiles = series.Percentiles(25.0, 50.0, 75.0);
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

        var deviation = current < lowerBound ? (lowerBound - current) / iqr : (current - upperBound) / iqr;

        return new AdvancedAnomaly(
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            metricName,
            current > upperBound ? AnomalyType.Spike : AnomalyType.Drop,
            Math.Min(deviation / (multiplier * 2.0), 1.0),
            0.8,
            current,
            lowerBound,
            upperBound,
            $"IQR anomaly: value {current:F2} outside [{lowerBound:F2}, {upperBound:F2}]",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "iqr" });
    }

    private static AdvancedAnomaly? MovingAverageDetection(
        string metricName,
        TimeSeries series,
        int window,
        double threshold)
    {
        var values = series.GetValues();
        if (values.Length < window)
        {
            return null;
        }

        var current = values[^1];
        var movingAverage = values[^window..].Average();
        if (movingAverage == 0.0)
        {
            return null;
        }

        var deviation = Math.Abs(current - movingAverage) / Math.Abs(movingAverage);
        if (deviation <= threshold)
        {
            return null;
        }

        return new AdvancedAnomaly(
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            metricName,
            current > movingAverage ? AnomalyType.Spike : AnomalyType.Drop,
            Math.Min(deviation / (threshold * 2.0), 1.0),
            0.7,
            current,
            movingAverage * (1.0 - threshold),
            movingAverage * (1.0 + threshold),
            $"Moving average anomaly: {deviation:F2} dev from MA {movingAverage:F2}",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "moving_average" });
    }

    private static AdvancedAnomaly? ExponentialSmoothingDetection(
        string metricName,
        TimeSeries series,
        double alpha,
        double threshold)
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

        return new AdvancedAnomaly(
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            metricName,
            current > smoothed ? AnomalyType.Spike : AnomalyType.Drop,
            Math.Min(deviation / (threshold * 2.0), 1.0),
            0.75,
            current,
            smoothed * (1.0 - threshold),
            smoothed * (1.0 + threshold),
            $"Exponential smoothing anomaly: {deviation:F2} dev from smoothed {smoothed:F2}",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "exponential_smoothing" });
    }

    public IReadOnlyList<AdvancedAnomaly> GetRecentAlerts(int limit)
    {
        lock (_alertGate)
        {
            return [.. _alerts.AsEnumerable().Reverse().Take(limit)];
        }
    }

    public void ClearAlerts()
    {
        lock (_alertGate)
        {
            _alerts.Clear();
        }
    }
}
