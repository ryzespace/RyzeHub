using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Pipeline;

public interface IPipelineHealthService
{
    Task<HealthStatus> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Aggregates source, destination and RyzeAuth availability into a single health document.
/// A probe never throws: an unreachable dependency is reported as down.
/// </summary>
public sealed class PipelineHealthService(
    ILogger<PipelineHealthService> logger,
    ISourceTicketClient source,
    IDestinationTicketClient destination,
    IHubPlatform hub,
    ISystemClock clock,
    IRyzeAuthClient? ryzeAuth = null) : IPipelineHealthService
{
    public async Task<HealthStatus> CheckAsync(CancellationToken cancellationToken)
    {
        var (sourceHealthy, sourceLatency) = await ProbeAsync(source.HealthCheckAsync, cancellationToken);
        var (destinationHealthy, destinationLatency) = await ProbeAsync(destination.HealthCheckAsync, cancellationToken);

        hub.UpdateServiceHealth("RyzeSpace.Client", sourceHealthy, (long)sourceLatency.TotalMilliseconds);
        hub.UpdateServiceHealth("RyzeSpace.HelpCenter", destinationHealthy, (long)destinationLatency.TotalMilliseconds);

        var authHealth = await CheckAuthAsync(cancellationToken);

        var authDown = authHealth is { Status: "down" };
        var status = sourceHealthy && destinationHealthy && !authDown
            ? "healthy"
            : sourceHealthy || destinationHealthy
                ? "degraded"
                : "unhealthy";

        var now = clock.UtcNow;
        return new HealthStatus(
            status,
            now,
            new ServiceHealth("client_dashboard", sourceHealthy ? "up" : "down", (long)sourceLatency.TotalMilliseconds, now),
            new ServiceHealth("helpcenter", destinationHealthy ? "up" : "down", (long)destinationLatency.TotalMilliseconds, now),
            new PipelineHealth(status, PipelineMetrics.UptimeSeconds, 0, 0.0),
            hub.HealthStatus(),
            authHealth);
    }

    private async Task<AuthHealthStatus?> CheckAuthAsync(CancellationToken cancellationToken)
    {
        if (ryzeAuth is null)
        {
            return null;
        }

        try
        {
            var (healthy, latency, jwks, grpc) = await ryzeAuth.HealthCheckAsync(cancellationToken);
            hub.UpdateServiceHealth("RyzeAuth", healthy, (long)latency.TotalMilliseconds);

            return new AuthHealthStatus(
                healthy ? "up" : "down",
                "configured",
                jwks,
                grpc,
                (long)latency.TotalMilliseconds,
                clock.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "RyzeAuth health check failed");
            hub.UpdateServiceHealth("RyzeAuth", healthy: false, latencyMs: 0);
            return new AuthHealthStatus("down", "configured", false, false, 0, clock.UtcNow);
        }
    }

    private static async Task<(bool Healthy, TimeSpan Latency)> ProbeAsync(
        Func<CancellationToken, Task<(bool, TimeSpan)>> probe,
        CancellationToken cancellationToken)
    {
        try
        {
            return await probe(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, TimeSpan.Zero);
        }
    }
}
