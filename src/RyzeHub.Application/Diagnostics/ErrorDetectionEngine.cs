using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.Application.Diagnostics;

/// <summary>
/// Pattern based error recognition with statistical anomaly detection and predictive hints.
/// </summary>
public sealed class ErrorDetectionEngine : IErrorDetectionEngine
{
    private const int MaxHistorySize = 1_000;
    private const int MaxRecentErrors = 10_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly ConcurrentDictionary<string, ErrorPattern> _patterns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Regex> _compiledPatterns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ErrorCategory, long> _errorCounts = new();
    private readonly ConcurrentDictionary<string, double> _anomalyThresholds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Queue<double>> _metricHistory = new(StringComparer.Ordinal);
    private readonly Queue<DetectedError> _recentErrors = new();
    private readonly Lock _errorGate = new();
    private readonly ILogger<ErrorDetectionEngine> _logger;

    public ErrorDetectionEngine(ILogger<ErrorDetectionEngine> logger)
    {
        _logger = logger;
        InitializeDefaultPatterns();
        _logger.LogInformation("Initialized error detection engine with {Count} patterns", _patterns.Count);
    }

    private void InitializeDefaultPatterns()
    {
        ErrorPattern[] defaults =
        [
            new()
            {
                Id = "network_timeout",
                Name = "Network Timeout",
                Description = "Connection or request timeout",
                RegexPattern = "(?i)(timeout|timed out|deadline exceeded)",
                Category = ErrorCategory.Timeout,
                Severity = ErrorSeverity.Medium,
                AutoResolve = false
            },
            new()
            {
                Id = "auth_failure",
                Name = "Authentication Failure",
                Description = "Authentication or authorization error",
                RegexPattern = "(?i)(unauthorized|forbidden|auth.*fail|invalid.*token|401|403)",
                Category = ErrorCategory.Authentication,
                Severity = ErrorSeverity.High,
                AutoResolve = false
            },
            new()
            {
                Id = "rate_limit",
                Name = "Rate Limit Exceeded",
                Description = "API rate limit exceeded",
                RegexPattern = "(?i)(rate.?limit|too many requests|429|throttl)",
                Category = ErrorCategory.RateLimit,
                Severity = ErrorSeverity.Medium,
                AutoResolve = true
            },
            new()
            {
                Id = "validation_error",
                Name = "Validation Error",
                Description = "Data validation failed",
                RegexPattern = "(?i)(validation.*fail|invalid.*data|required.*field|missing.*field)",
                Category = ErrorCategory.Validation,
                Severity = ErrorSeverity.Low,
                AutoResolve = false
            },
            new()
            {
                Id = "connection_error",
                Name = "Connection Error",
                Description = "Network connection failure",
                RegexPattern = "(?i)(connection.*refused|connection.*reset|network.*unreachable|503)",
                Category = ErrorCategory.Network,
                Severity = ErrorSeverity.High,
                AutoResolve = false
            },
            new()
            {
                Id = "ryzeauth_denied",
                Name = "RyzeAuth Denied",
                Description = "RyzeAuth rejected an API key or token",
                RegexPattern = "(?i)(api key (revoked|expired|inactive)|missing scope|introspection failed|organization disabled)",
                Category = ErrorCategory.Authorization,
                Severity = ErrorSeverity.Critical,
                AutoResolve = false
            }
        ];

        foreach (var pattern in defaults)
        {
            AddPattern(pattern);
        }
    }

    public DetectedError? DetectError(string message, string source)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

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

            lock (_errorGate)
            {
                _recentErrors.Enqueue(detected);
                while (_recentErrors.Count > MaxRecentErrors)
                {
                    _recentErrors.Dequeue();
                }
            }

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

    public void SetAnomalyThreshold(string metricName, double threshold) => _anomalyThresholds[metricName] = threshold;

    public void RecordMetric(string metricName, double value)
    {
        var history = _metricHistory.GetOrAdd(metricName, static _ => new Queue<double>());
        lock (history)
        {
            history.Enqueue(value);
            while (history.Count > MaxHistorySize)
            {
                history.Dequeue();
            }
        }
    }

    public IReadOnlyList<AnomalyResult> DetectAnomalies()
    {
        var anomalies = new List<AnomalyResult>();

        foreach (var (metricName, history) in _metricHistory)
        {
            double[] values;
            lock (history)
            {
                if (history.Count < 10)
                {
                    continue;
                }

                values = [.. history];
            }

            var current = values[^1];
            var mean = values.Average();
            var variance = values.Sum(value => Math.Pow(value - mean, 2)) / values.Length;
            var stdDev = Math.Sqrt(variance);

            var threshold = _anomalyThresholds.GetValueOrDefault(metricName, 2.0);
            var deviation = stdDev > 0 ? Math.Abs(current - mean) / stdDev : 0.0;

            if (deviation <= threshold)
            {
                continue;
            }

            var severity = deviation > threshold * 2.0
                ? ErrorSeverity.Critical
                : deviation > threshold * 1.5
                    ? ErrorSeverity.High
                    : ErrorSeverity.Medium;

            anomalies.Add(new AnomalyResult(
                DateTimeOffset.UtcNow,
                metricName,
                current,
                threshold,
                deviation,
                severity,
                $"Metric {metricName} is {deviation:F2} std devs from mean (current: {current:F2}, mean: {mean:F2})"));

            _logger.LogWarning("Anomaly detected: {MetricName}", metricName);
        }

        return anomalies;
    }

    public IReadOnlyDictionary<ErrorCategory, long> GetStatistics() =>
        _errorCounts.ToDictionary(pair => pair.Key, pair => pair.Value);

    public IReadOnlyList<DetectedError> GetRecentErrors(int limit)
    {
        lock (_errorGate)
        {
            return [.. _recentErrors.Reverse().Take(limit)];
        }
    }

    public IReadOnlyList<ErrorPattern> GetPatternStats() =>
        [.. _patterns.Values.OrderBy(pattern => pattern.Id, StringComparer.Ordinal)];

    public IReadOnlyList<IReadOnlyList<DetectedError>> CorrelateErrors(TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        DetectedError[] recent;

        lock (_errorGate)
        {
            recent = [.. _recentErrors.Where(error => now - error.Timestamp < window)];
        }

        return
        [
            .. recent
                .GroupBy(error => error.Source, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => (IReadOnlyList<DetectedError>)[.. group])
        ];
    }

    public IReadOnlyList<string> PredictIssues()
    {
        var predictions = new List<string>();

        foreach (var (category, count) in _errorCounts)
        {
            if (count > 100)
            {
                predictions.Add($"High {category} error count: {count} errors detected");
            }
        }

        predictions.AddRange(DetectAnomalies().Select(anomaly => anomaly.Description));
        return predictions;
    }
}
