using Microsoft.Extensions.DependencyInjection;
using RyzeHub.Application;

namespace RyzeHub.Cli.Commands;

internal sealed class ErrorsCommand : ICommandHandler
{
    public string Name => "errors";

    public string Usage => "errors <analyze|stats|anomalies|patterns|predictions>";

    public string Description => "Error detection engine";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var engine = provider.GetRequiredService<IErrorDetectionEngine>();
        var action = arguments.Action("stats");

        switch (action)
        {
            case "analyze":
                var detected = engine.DetectError(
                    arguments.RequireValue("message"),
                    arguments.GetValue("source") ?? "unknown");
                ConsoleOutput.WriteJson(detected);
                return Task.FromResult(ExitCodes.Success);

            case "stats":
                ConsoleOutput.WriteJson(engine.GetStatistics());
                return Task.FromResult(ExitCodes.Success);

            case "anomalies":
                ConsoleOutput.WriteJson(engine.DetectAnomalies());
                return Task.FromResult(ExitCodes.Success);

            case "patterns":
                ConsoleOutput.WriteJson(engine.GetPatternStats());
                return Task.FromResult(ExitCodes.Success);

            case "predictions":
                ConsoleOutput.WriteJson(engine.PredictIssues());
                return Task.FromResult(ExitCodes.Success);

            default:
                ConsoleOutput.WriteError($"Unknown errors action: {action}");
                return Task.FromResult(ExitCodes.Failure);
        }
    }
}

internal sealed class AuthCommand : ICommandHandler
{
    public string Name => "auth";

    public string Usage => "auth <health|introspect-key|introspect-token>";

    public string Description => "RyzeAuth control plane";

    public async Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var client = provider.GetService<IRyzeAuthClient>();
        if (client is null)
        {
            ConsoleOutput.WriteError("RyzeAuth integration is not configured.");
            return ExitCodes.Failure;
        }

        var action = arguments.Action("health");

        switch (action)
        {
            case "health":
                var (healthy, latency, jwks, grpc) = await client.HealthCheckAsync(cancellationToken);
                ConsoleOutput.WriteJson(new
                {
                    healthy,
                    latencyMs = (long)latency.TotalMilliseconds,
                    jwksReachable = jwks,
                    grpcReachable = grpc
                });
                return healthy ? ExitCodes.Success : ExitCodes.Failure;

            case "introspect-key":
                var keyResult = await client.IntrospectApiKeyAsync(
                    arguments.RequireValue("api-key"),
                    arguments.GetValue("scope") ?? "hub:tickets:transfer",
                    cancellationToken);
                ConsoleOutput.WriteJson(keyResult);
                return keyResult.Active ? ExitCodes.Success : ExitCodes.Failure;

            case "introspect-token":
                var tokenResult = await client.IntrospectTokenAsync(
                    arguments.RequireValue("token"),
                    cancellationToken);
                ConsoleOutput.WriteJson(tokenResult);
                return tokenResult.Active ? ExitCodes.Success : ExitCodes.Failure;

            default:
                ConsoleOutput.WriteError($"Unknown auth action: {action}");
                return ExitCodes.Failure;
        }
    }
}
