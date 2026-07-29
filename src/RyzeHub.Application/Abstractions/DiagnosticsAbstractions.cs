using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application;

public interface IErrorDetectionEngine
{
    DetectedError? DetectError(string message, string source);

    void AddPattern(ErrorPattern pattern);

    void SetAnomalyThreshold(string metricName, double threshold);

    void RecordMetric(string metricName, double value);

    IReadOnlyList<AnomalyResult> DetectAnomalies();

    IReadOnlyDictionary<ErrorCategory, long> GetStatistics();

    IReadOnlyList<DetectedError> GetRecentErrors(int limit);

    IReadOnlyList<ErrorPattern> GetPatternStats();

    IReadOnlyList<IReadOnlyList<DetectedError>> CorrelateErrors(TimeSpan window);

    IReadOnlyList<string> PredictIssues();
}

public interface IAnomalyDetector
{
    void TrackMetric(string metricName, int maxHistory);

    void Record(string metricName, double value);

    IReadOnlyList<AdvancedAnomaly> Detect();

    IReadOnlyList<AdvancedAnomaly> GetRecentAlerts(int limit);

    void ClearAlerts();
}
