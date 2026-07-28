using RyzeHub.Application.Diagnostics;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.UnitTests;

public sealed class ErrorDetectionTests
{
    private static ErrorDetectionEngine CreateEngine() => new(TestSupport.Logger<ErrorDetectionEngine>());

    [Fact]
    public void DetectsTimeoutErrors()
    {
        var detected = CreateEngine().DetectError("Request timeout after 30s", "api_client");

        detected.Should().NotBeNull();
        detected!.Category.Should().Be(ErrorCategory.Timeout);
    }

    [Fact]
    public void DetectsAuthenticationFailures()
    {
        var detected = CreateEngine().DetectError("401 Unauthorized: Invalid token", "api_client");

        detected.Should().NotBeNull();
        detected!.Category.Should().Be(ErrorCategory.Authentication);
        detected.Severity.Should().Be(ErrorSeverity.High);
    }

    [Fact]
    public void DetectsRyzeAuthDenials()
    {
        var detected = CreateEngine().DetectError("RyzeAuth introspection failed for scoped key", "ryzeauth");

        detected.Should().NotBeNull();
        detected!.Category.Should().Be(ErrorCategory.Authorization);
        detected.Severity.Should().Be(ErrorSeverity.Critical);
    }

    [Fact]
    public void ReturnsNullForUnmatchedMessages()
    {
        CreateEngine().DetectError("everything is fine", "api_client").Should().BeNull();
    }

    [Fact]
    public void CollectsStatisticsPerCategory()
    {
        var engine = CreateEngine();
        engine.DetectError("Timeout error", "source1");
        engine.DetectError("Another timeout", "source2");
        engine.DetectError("Auth failure", "source1");

        var stats = engine.GetStatistics();

        stats[ErrorCategory.Timeout].Should().Be(2);
        stats[ErrorCategory.Authentication].Should().Be(1);
    }

    [Fact]
    public void DetectsMetricAnomalies()
    {
        var engine = CreateEngine();
        engine.SetAnomalyThreshold("error_rate", 2.0);

        for (var index = 0; index < 20; index++)
        {
            engine.RecordMetric("error_rate", 5.0);
        }

        engine.RecordMetric("error_rate", 50.0);

        engine.DetectAnomalies().Should().NotBeEmpty();
    }

    [Fact]
    public void CorrelatesErrorsFromTheSameSource()
    {
        var engine = CreateEngine();
        engine.DetectError("Timeout one", "helpcenter");
        engine.DetectError("Timeout two", "helpcenter");
        engine.DetectError("Timeout three", "client");

        var correlations = engine.CorrelateErrors(TimeSpan.FromMinutes(5));

        correlations.Should().ContainSingle();
        correlations[0].Should().HaveCount(2);
    }

    [Fact]
    public void RecentErrorsAreReturnedNewestFirst()
    {
        var engine = CreateEngine();
        engine.DetectError("first timeout", "a");
        engine.DetectError("second timeout", "b");

        var recent = engine.GetRecentErrors(2);

        recent.Should().HaveCount(2);
        recent[0].Source.Should().Be("b");
    }
}

public sealed class AnomalyDetectorTests
{
    private static AnomalyDetector CreateDetector() => new(TestSupport.Logger<AnomalyDetector>());

    [Fact]
    public void ZScoreDetectionFlagsOutliers()
    {
        var detector = CreateDetector();
        detector.TrackMetric("test_metric", 100);

        for (var index = 0; index < 20; index++)
        {
            detector.Record("test_metric", 10.0);
        }

        detector.Record("test_metric", 100.0);

        detector.Detect().Should().NotBeEmpty();
    }

    [Fact]
    public void IqrDetectionFlagsExtremeValues()
    {
        var detector = CreateDetector();
        detector.TrackMetric("test_metric", 100);

        for (var index = 0; index < 20; index++)
        {
            detector.Record("test_metric", 50.0 + index % 3);
        }

        detector.Record("test_metric", 200.0);

        detector.Detect().Should().Contain(anomaly => anomaly.AnomalyType == AnomalyType.Spike);
    }

    [Fact]
    public void StableSeriesProducesNoAnomalies()
    {
        var detector = CreateDetector();
        detector.TrackMetric("stable", 100);

        for (var index = 0; index < 30; index++)
        {
            detector.Record("stable", 42.0);
        }

        detector.Detect().Should().BeEmpty();
    }

    [Fact]
    public void TimeSeriesComputesStatistics()
    {
        var series = new TimeSeries("test", 100);
        for (var index = 0; index < 10; index++)
        {
            series.AddPoint(DateTimeOffset.UtcNow, index);
        }

        series.Mean().Should().Be(4.5);
        series.StandardDeviation().Should().BeGreaterThan(0);
        series.Percentiles(50.0)[50.0].Should().BeInRange(4.0, 5.0);
    }

    [Fact]
    public void AlertsAreRetainedAndClearable()
    {
        var detector = CreateDetector();
        detector.TrackMetric("spiky", 100);

        for (var index = 0; index < 20; index++)
        {
            detector.Record("spiky", 5.0);
        }

        detector.Record("spiky", 500.0);
        detector.Detect();

        detector.GetRecentAlerts(10).Should().NotBeEmpty();
        detector.ClearAlerts();
        detector.GetRecentAlerts(10).Should().BeEmpty();
    }
}
