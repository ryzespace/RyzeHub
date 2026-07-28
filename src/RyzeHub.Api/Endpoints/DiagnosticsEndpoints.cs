using RyzeHub.Application;

namespace RyzeHub.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/diagnostics").WithTags("Diagnostics").RequireAuthorization();

        group.MapPost("/analyze", (IErrorDetectionEngine engine, AnalyzeErrorRequest request) =>
        {
            var detected = engine.DetectError(request.Message, request.Source ?? "unknown");
            return detected is null ? Results.NoContent() : Results.Ok(detected);
        });

        group.MapGet("/statistics", (IErrorDetectionEngine engine) => Results.Ok(engine.GetStatistics()));
        group.MapGet("/errors", (IErrorDetectionEngine engine, int limit = 20) => Results.Ok(engine.GetRecentErrors(limit)));
        group.MapGet("/patterns", (IErrorDetectionEngine engine) => Results.Ok(engine.GetPatternStats()));
        group.MapGet("/anomalies", (IErrorDetectionEngine engine) => Results.Ok(engine.DetectAnomalies()));
        group.MapGet("/predictions", (IErrorDetectionEngine engine) => Results.Ok(engine.PredictIssues()));

        group.MapGet("/correlations", (IErrorDetectionEngine engine, int windowSeconds = 300) =>
            Results.Ok(engine.CorrelateErrors(TimeSpan.FromSeconds(windowSeconds))));

        group.MapGet("/advanced-anomalies", (IAnomalyDetector detector) => Results.Ok(detector.Detect()));

        group.MapPost("/metrics/{name}", (IAnomalyDetector detector, IErrorDetectionEngine engine, string name, RecordMetricRequest request) =>
        {
            detector.Record(name, request.Value);
            engine.RecordMetric(name, request.Value);
            return Results.Accepted();
        }).RequireAuthorization("PlatformWrite");

        return endpoints;
    }

    public sealed record AnalyzeErrorRequest(string Message, string? Source);

    public sealed record RecordMetricRequest(double Value);
}
