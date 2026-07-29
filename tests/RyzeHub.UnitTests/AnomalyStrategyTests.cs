using RyzeHub.Application.Diagnostics;
using RyzeHub.Application.Diagnostics.Detectors;
using RyzeHub.Domain.Diagnostics;

namespace RyzeHub.UnitTests;

/// <summary>Each strategy is stateless, so it can be driven with a hand-built series.</summary>
public sealed class AnomalyStrategyTests
{
    private static TimeSeries SeriesOf(params double[] values)
    {
        var series = new TimeSeries("metric", 1_000);
        var timestamp = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        foreach (var value in values)
        {
            series.AddPoint(timestamp, value);
            timestamp = timestamp.AddSeconds(1);
        }

        return series;
    }

    private static double[] Repeat(double value, int count) => [.. Enumerable.Repeat(value, count)];

    [Fact]
    public void ZScoreFlagsAnUpwardOutlierAsASpike()
    {
        var series = SeriesOf([.. Repeat(10.0, 20), 100.0]);

        var anomaly = new ZScoreStrategy(3.0).Detect("metric", series);

        anomaly.Should().NotBeNull();
        anomaly!.AnomalyType.Should().Be(AnomalyType.Spike);
        anomaly.Metadata["method"].Should().Be("z_score");
        anomaly.Severity.Should().BeInRange(0.0, 1.0);
        anomaly.Confidence.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void ZScoreFlagsADownwardOutlierAsADrop()
    {
        var series = SeriesOf([.. Repeat(100.0, 20), 1.0]);

        new ZScoreStrategy(3.0).Detect("metric", series)!.AnomalyType.Should().Be(AnomalyType.Drop);
    }

    [Fact]
    public void ZScoreIgnoresAFlatSeries()
    {
        // Zero standard deviation: no sample can be an outlier.
        new ZScoreStrategy().Detect("metric", SeriesOf(Repeat(42.0, 30))).Should().BeNull();
    }

    [Fact]
    public void ZScoreIgnoresNormalVariation()
    {
        var series = SeriesOf([10, 11, 9, 10, 12, 8, 10, 11, 9, 10, 11]);

        new ZScoreStrategy(3.0).Detect("metric", series).Should().BeNull();
    }

    [Fact]
    public void IqrFlagsAnExtremeValue()
    {
        var series = SeriesOf([.. Enumerable.Range(0, 20).Select(i => 50.0 + i % 3), 200.0]);

        var anomaly = new IqrStrategy(1.5).Detect("metric", series);

        anomaly.Should().NotBeNull();
        anomaly!.AnomalyType.Should().Be(AnomalyType.Spike);
        anomaly.Metadata["method"].Should().Be("iqr");
        anomaly.Value.Should().Be(200.0);
    }

    [Fact]
    public void IqrIgnoresASeriesWithNoSpread()
    {
        new IqrStrategy().Detect("metric", SeriesOf(Repeat(7.0, 25))).Should().BeNull();
    }

    [Fact]
    public void MovingAverageNeedsAFullWindow()
    {
        new MovingAverageStrategy(window: 10).Detect("metric", SeriesOf([1, 2, 3])).Should().BeNull();
    }

    [Fact]
    public void MovingAverageFlagsARelativeJump()
    {
        var series = SeriesOf([.. Repeat(10.0, 15), 100.0]);

        var anomaly = new MovingAverageStrategy(window: 10, threshold: 2.0).Detect("metric", series);

        anomaly.Should().NotBeNull();
        anomaly!.Metadata["method"].Should().Be("moving_average");
    }

    [Fact]
    public void ExponentialSmoothingFlagsADeviationFromTheSmoothedBaseline()
    {
        var series = SeriesOf([.. Repeat(10.0, 20), 500.0]);

        var anomaly = new ExponentialSmoothingStrategy(alpha: 0.3, threshold: 2.0).Detect("metric", series);

        anomaly.Should().NotBeNull();
        anomaly!.Metadata["method"].Should().Be("exponential_smoothing");
    }

    [Fact]
    public void ExponentialSmoothingIgnoresAStableSeries()
    {
        new ExponentialSmoothingStrategy(threshold: 2.0)
            .Detect("metric", SeriesOf(Repeat(20.0, 30)))
            .Should().BeNull();
    }

    [Fact]
    public void DetectorRunsOnlyTheStrategiesItWasGiven()
    {
        var detector = new AnomalyDetector(
            TestSupport.Logger<AnomalyDetector>(),
            [new ZScoreStrategy(3.0)]);

        detector.TrackMetric("metric", 100);
        foreach (var value in Repeat(10.0, 20))
        {
            detector.Record("metric", value);
        }

        detector.Record("metric", 100.0);

        var anomalies = detector.Detect();

        anomalies.Should().ContainSingle();
        anomalies[0].Metadata["method"].Should().Be("z_score");
    }

    [Fact]
    public void DetectorSkipsMetricsWithTooFewSamples()
    {
        var detector = new AnomalyDetector(TestSupport.Logger<AnomalyDetector>());
        detector.TrackMetric("sparse", 100);
        detector.Record("sparse", 1.0);
        detector.Record("sparse", 1000.0);

        detector.Detect().Should().BeEmpty();
    }
}

public sealed class TimeSeriesTests
{
    [Fact]
    public void EvictsTheOldestPointsBeyondTheCap()
    {
        var series = new TimeSeries("capped", 3);

        for (var index = 0; index < 10; index++)
        {
            series.AddPoint(DateTimeOffset.UtcNow, index);
        }

        series.Count.Should().Be(3);
        series.GetValues().Should().BeEquivalentTo(new[] { 7.0, 8.0, 9.0 });
    }

    [Fact]
    public void ComputesMeanAndSampleStandardDeviation()
    {
        var series = new TimeSeries("stats", 100);
        for (var index = 0; index < 10; index++)
        {
            series.AddPoint(DateTimeOffset.UtcNow, index);
        }

        series.Mean().Should().Be(4.5);
        series.StandardDeviation().Should().BeApproximately(3.0277, 0.001);
    }

    [Fact]
    public void ReturnsZeroStatisticsForAnEmptySeries()
    {
        var series = new TimeSeries("empty", 10);

        series.Mean().Should().Be(0.0);
        series.StandardDeviation().Should().Be(0.0);
        series.Percentiles(50.0).Should().BeEmpty();
    }

    [Fact]
    public void ComputesPercentiles()
    {
        var series = new TimeSeries("percentiles", 100);
        for (var index = 1; index <= 100; index++)
        {
            series.AddPoint(DateTimeOffset.UtcNow, index);
        }

        var percentiles = series.Percentiles(25.0, 50.0, 75.0);

        percentiles[25.0].Should().BeInRange(25.0, 26.0);
        percentiles[75.0].Should().BeInRange(75.0, 76.0);
    }
}
