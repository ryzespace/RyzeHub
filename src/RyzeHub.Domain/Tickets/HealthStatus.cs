using RyzeHub.Domain.Platform;

namespace RyzeHub.Domain.Tickets;

public sealed record ServiceHealth(string Name, string Status, long LatencyMs, DateTimeOffset LastCheck);

public sealed record PipelineHealth(string Status, long UptimeSeconds, long TicketsProcessed, double ErrorRate);

public sealed record HealthStatus(
    string Status,
    DateTimeOffset Timestamp,
    ServiceHealth Source,
    ServiceHealth Destination,
    PipelineHealth Pipeline,
    HubHealthStatus? Hub,
    AuthHealthStatus? Auth);

/// <summary>
/// Reachability of the RyzeAuth control plane that RyzeHub depends on.
/// </summary>
public sealed record AuthHealthStatus(
    string Status,
    string Authority,
    bool JwksReachable,
    bool GrpcReachable,
    long LatencyMs,
    DateTimeOffset LastCheck);
