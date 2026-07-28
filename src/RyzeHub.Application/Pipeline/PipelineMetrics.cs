using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RyzeHub.Application.Pipeline;

/// <summary>
/// OpenTelemetry instruments exported through the Prometheus scraping endpoint.
/// </summary>
public sealed class PipelineMetrics : IDisposable
{
    public const string MeterName = "RyzeHub.Pipeline";

    private static readonly Stopwatch ProcessUptime = Stopwatch.StartNew();

    private readonly Meter _meter;
    private readonly Counter<long> _ticketsFetched;
    private readonly Counter<long> _ticketsTransferred;
    private readonly Counter<long> _ticketsFailed;
    private readonly Counter<long> _ticketsFiltered;
    private readonly Counter<long> _pipelineRuns;
    private readonly Counter<long> _authDenials;
    private readonly Histogram<double> _transferDuration;
    private long _activeConnections;

    public PipelineMetrics()
    {
        _meter = new Meter(MeterName);
        _ticketsFetched = _meter.CreateCounter<long>("pipeline_tickets_fetched_total", description: "Total tickets fetched from source");
        _ticketsTransferred = _meter.CreateCounter<long>("pipeline_tickets_transferred_total", description: "Total tickets successfully transferred");
        _ticketsFailed = _meter.CreateCounter<long>("pipeline_tickets_failed_total", description: "Total tickets that failed to transfer");
        _ticketsFiltered = _meter.CreateCounter<long>("pipeline_tickets_filtered_total", description: "Total tickets filtered out");
        _pipelineRuns = _meter.CreateCounter<long>("pipeline_runs_total", description: "Total pipeline runs");
        _authDenials = _meter.CreateCounter<long>("pipeline_ryzeauth_denials_total", description: "Requests denied by RyzeAuth");
        _transferDuration = _meter.CreateHistogram<double>("pipeline_transfer_duration_ms", unit: "ms", description: "Transfer duration in milliseconds");

        _meter.CreateObservableGauge("pipeline_active_connections", () => Interlocked.Read(ref _activeConnections), description: "Active connections to APIs");
        _meter.CreateObservableGauge("pipeline_uptime_seconds", () => (long)ProcessUptime.Elapsed.TotalSeconds, description: "Process uptime in seconds");
    }

    public static long UptimeSeconds => (long)ProcessUptime.Elapsed.TotalSeconds;

    public void RecordFetched(int count) => _ticketsFetched.Add(count);

    public void RecordTransferred(int count) => _ticketsTransferred.Add(count);

    public void RecordFailed(int count) => _ticketsFailed.Add(count);

    public void RecordFiltered(int count) => _ticketsFiltered.Add(count);

    public void RecordRun() => _pipelineRuns.Add(1);

    public void RecordAuthDenial(string reason) => _authDenials.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordTransferDuration(double milliseconds) => _transferDuration.Record(milliseconds);

    public void SetActiveConnections(long count) => Interlocked.Exchange(ref _activeConnections, count);

    public void Dispose() => _meter.Dispose();
}
