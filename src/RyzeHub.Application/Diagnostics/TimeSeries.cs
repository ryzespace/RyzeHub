namespace RyzeHub.Application.Diagnostics;

/// <summary>Rolling window of samples for a single metric, with the statistics detectors need.</summary>
public sealed class TimeSeries(string name, int maxSize)
{
    private readonly Queue<(DateTimeOffset Timestamp, double Value)> _data = new();
    private readonly Lock _gate = new();

    public string Name { get; } = name;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _data.Count;
            }
        }
    }

    public void AddPoint(DateTimeOffset timestamp, double value)
    {
        lock (_gate)
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
        lock (_gate)
        {
            return [.. _data.Select(static point => point.Value)];
        }
    }

    public double Mean()
    {
        var values = GetValues();
        return values.Length == 0 ? 0.0 : values.Average();
    }

    /// <summary>Sample standard deviation (Bessel corrected).</summary>
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
