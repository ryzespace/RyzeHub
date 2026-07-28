using RyzeHub.Domain.Platform;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application;

public interface ITicketPipeline
{
    Task<PipelineRunMetrics> RunOnceAsync(CancellationToken cancellationToken);

    Task RunContinuousAsync(CancellationToken cancellationToken);

    Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken);

    HubPlatformSnapshot HubSnapshot();

    IHubPlatform Hub { get; }

    HubPlatformSnapshot SeedHubDemo(string userId);
}

public sealed record PipelineRunMetrics
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int FetchedCount { get; set; }
    public int FilteredCount { get; set; }
    public int ValidCount { get; set; }
    public int TransferredCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; init; } = [];

    public void Finish(DateTimeOffset now)
    {
        FinishedAt = now;
        DurationSeconds = (now - StartedAt).TotalSeconds;
    }
}
