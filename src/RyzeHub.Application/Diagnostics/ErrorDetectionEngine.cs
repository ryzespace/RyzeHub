using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics;

/// <summary>
/// Matches log messages against known error signatures and correlates the results.
/// Pattern definitions live in <see cref="DefaultErrorPatterns"/> and the statistical
/// scan in <see cref="MetricAnomalyScanner"/>.
/// </summary>
public sealed class ErrorDetectionEngine : IErrorDetectionEngine
{
    private const int MaxRecentErrors = 10_000;
    private const long PredictionErrorThreshold = 100;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly ConcurrentDictionary<string, ErrorPattern> _patterns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Regex> _compiledPatterns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ErrorCategory, long> _errorCounts = new();
    private readonly BoundedLog<DetectedError> _recentErrors = new(MaxRecentErrors);
    private readonly MetricAnomalyScanner _scanner;
    private readonly ILogger<ErrorDetectionEngine> _logger;

    public ErrorDetectionEngine(ILogger<ErrorDetectionEngine> logger)
    {
        _logger = logger;
        _scanner = new MetricAnomalyScanner(logger);

        foreach (var pattern in DefaultErrorPatterns.All)
        {
            AddPattern(pattern);
        }

        _logger.LogInformation("Initialized error detection engine with {Count} patterns", _patterns.Count);
    }

    public DetectedError? DetectError(string message, string source)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        // Ordered by id so the same message always resolves to the same pattern.
        foreach (var pattern in _patterns.Values.OrderBy(pattern => pattern.Id, StringComparer.Ordinal))
        {
            if (!_compiledPatterns.TryGetValue(pattern.Id, out var regex) || !regex.IsMatch(message))
            {
                continue;
            }

            var detected = new DetectedError(
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow,
                message,
                pattern.Category,
                pattern.Severity,
                source,
                new Dictionary<string, string>(StringComparer.Ordinal),
                pattern.Id,
                CorrelationId: null);

            _patterns[pattern.Id] = pattern with
            {
                OccurrenceCount = pattern.OccurrenceCount + 1,
                LastSeen = DateTimeOffset.UtcNow
            };

            _errorCounts.AddOrUpdate(pattern.Category, 1, static (_, current) => current + 1);
            _recentErrors.Append(detected);

            _logger.LogDebug("Detected error pattern: {PatternName} in {Source}", pattern.Name, source);
            return detected;
        }

        return null;
    }

    public void AddPattern(ErrorPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        _patterns[pattern.Id] = pattern;
        _compiledPatterns[pattern.Id] = new Regex(pattern.RegexPattern, RegexOptions.None, RegexTimeout);
        _logger.LogDebug("Registered error pattern: {PatternName}", pattern.Name);
    }

    public void SetAnomalyThreshold(string metricName, double threshold) =>
        _scanner.SetThreshold(metricName, threshold);

    public void RecordMetric(string metricName, double value) => _scanner.Record(metricName, value);

    public IReadOnlyList<AnomalyResult> DetectAnomalies() => _scanner.Scan();

    public IReadOnlyDictionary<ErrorCategory, long> GetStatistics() =>
        _errorCounts.ToDictionary(pair => pair.Key, pair => pair.Value);

    public IReadOnlyList<DetectedError> GetRecentErrors(int limit) => _recentErrors.Recent(limit);

    public IReadOnlyList<ErrorPattern> GetPatternStats() =>
        [.. _patterns.Values.OrderBy(pattern => pattern.Id, StringComparer.Ordinal)];

    /// <summary>Groups recent errors by source; a source with more than one error is correlated.</summary>
    public IReadOnlyList<IReadOnlyList<DetectedError>> CorrelateErrors(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;

        return
        [
            .. _recentErrors.Snapshot()
                .Where(error => error.Timestamp >= cutoff)
                .GroupBy(error => error.Source, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => (IReadOnlyList<DetectedError>)[.. group])
        ];
    }

    public IReadOnlyList<string> PredictIssues()
    {
        var predictions = _errorCounts
            .Where(pair => pair.Value > PredictionErrorThreshold)
            .Select(pair => $"High {pair.Key} error count: {pair.Value} errors detected")
            .ToList();

        predictions.AddRange(DetectAnomalies().Select(anomaly => anomaly.Description));
        return predictions;
    }
}
