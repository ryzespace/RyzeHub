using RyzeHub.Application;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Api.Endpoints;

public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pipeline").WithTags("Pipeline");

        group.MapPost("/run", RunOnce)
            .RequireAuthorization("TicketTransfer")
            .RequireRateLimiting("pipeline-run")
            .Produces<PipelineRunMetrics>(StatusCodes.Status200OK);

        group.MapGet("/health", HealthAsync)
            .AllowAnonymous()
            .Produces<HealthStatus>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> RunOnce(ITicketPipeline pipeline, CancellationToken cancellationToken)
    {
        var metrics = await pipeline.RunOnceAsync(cancellationToken);
        return Results.Ok(metrics);
    }

    private static async Task<IResult> HealthAsync(ITicketPipeline pipeline, CancellationToken cancellationToken)
    {
        var health = await pipeline.HealthCheckAsync(cancellationToken);
        return health.Status == "unhealthy"
            ? Results.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(health);
    }
}
