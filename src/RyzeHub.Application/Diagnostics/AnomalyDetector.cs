using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RyzeHub.Application.Diagnostics.Detectors;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics;

/// <summary>
/// Tracks metric time series and runs every registered detection strategy over them.
/// The statistics live in <see cref="TimeSeries"/> and the tests in the strategies,
/// so this type only owns registration, sampling and alert retention.
/// </summary>
public sealed class AnomalyDetector : IAnomalyDetector
{
    private const int MinimumSamples = 10;
    private const int DefaultHistorySize = 1_000;
    private const int MaxAlerts = 10_000;

    private readonly ConcurrentDictionary<string, TimeSeries> _timeSeries = new(StringComparer.Ordinal);
    private readonly BoundedLog<AdvancedAnomaly> _alerts = new(MaxAlerts);
    private readonly IReadOnlyList<IAnomalyDetectionStrategy> _strategies;
    private readonly ILogger<AnomalyDetector> _logger;

    public AnomalyDetector(ILogger<AnomalyDetector> logger)
        : this(logger, [new ZScoreStrategy(), new IqrStrategy(), new MovingAverageStrategy()])
    {
    }

    public AnomalyDetector(ILogger<AnomalyDetector> logger, IReadOnlyList<IAnomalyDetectionStrategy> strategies)
    {
        _logger = logger;
        _strategies = strategies;
        _logger.LogInformation("Initialized anomaly detector with {Count} strategies", strategies.Count);
    }

    public void TrackMetric(string metricName, int maxHistory) =>
        _timeSeries[metricName] = new TimeSeries(metricName, maxHistory);

    public void Record(string metricName, double value) =>
        _timeSeries
            .GetOrAdd(metricName, static name => new TimeSeries(name, DefaultHistorySize))
            .AddPoint(DateTimeOffset.UtcNow, value);

    public IReadOnlyList<AdvancedAnomaly> Detect()
    {
        var anomalies = new List<AdvancedAnomaly>();

        foreach (var (metricName, series) in _timeSeries)
        {
            // Statistics over a handful of points are not meaningful.
            if (series.Count < MinimumSamples)
            {
                continue;
            }

            foreach (var strategy in _strategies)
            {
                if (strategy.Detect(metricName, series) is { } anomaly)
                {
                    anomalies.Add(anomaly);
                    _alerts.Append(anomaly);
                }
            }
        }

        if (anomalies.Count > 0)
        {
            _logger.LogWarning("Detected {Count} anomalies", anomalies.Count);
        }

        return anomalies;
    }

    public IReadOnlyList<AdvancedAnomaly> GetRecentAlerts(int limit) => _alerts.Recent(limit);

    public void ClearAlerts() => _alerts.Clear();
}
