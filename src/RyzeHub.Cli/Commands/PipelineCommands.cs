using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform;

namespace RyzeHub.Cli.Commands;

internal sealed class RunCommand : ICommandHandler
{
    public string Name => "run";

    public string Usage => "run [--continuous]";

    public string Description => "Execute the ticket pipeline once or on a poll loop";

    public async Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var pipeline = provider.GetRequiredService<ITicketPipeline>();

        if (arguments.HasFlag("continuous"))
        {
            await pipeline.RunContinuousAsync(cancellationToken);
            return ExitCodes.Success;
        }

        var metrics = await pipeline.RunOnceAsync(cancellationToken);
        ConsoleOutput.WriteJson(metrics);

        if (metrics.FailedCount > 0)
        {
            ConsoleOutput.WriteError($"{metrics.FailedCount} tickets failed!");
            return ExitCodes.Failure;
        }

        return ExitCodes.Success;
    }
}

internal sealed class HealthCommand : ICommandHandler
{
    public string Name => "health";

    public string Usage => "health";

    public string Description => "Pipeline + RyzeAuth health check";

    public async Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var health = await provider.GetRequiredService<ITicketPipeline>().HealthCheckAsync(cancellationToken);
        ConsoleOutput.WriteJson(health);
        return health.Status == "unhealthy" ? ExitCodes.Failure : ExitCodes.Success;
    }
}

internal sealed class ValidateCommand : ICommandHandler
{
    public string Name => "validate";

    public string Usage => "validate";

    public string Description => "Validate configuration and print the effective settings";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var pipeline = Options<PipelineOptions>(provider);
        var source = Options<SourceOptions>(provider);
        var destination = Options<DestinationOptions>(provider);
        var security = Options<SecurityOptions>(provider);
        var auth = Options<RyzeAuthOptions>(provider);

        var errors = new List<string>();
        Require(errors, source.BaseUrl, "Source:BaseUrl");
        Require(errors, source.ApiKey, "Source:ApiKey");
        Require(errors, destination.BaseUrl, "Destination:BaseUrl");
        Require(errors, destination.ApiKey, "Destination:ApiKey");
        Require(errors, security.EncryptionKey, "Security:EncryptionKey");
        Require(errors, auth.Authority, "RyzeAuth:Authority");

        ConsoleOutput.WriteJson(new
        {
            valid = errors.Count == 0,
            errors,
            pipeline = new { pipeline.AutoCategorize, pipeline.AutoPriority, pipeline.Deduplicate },
            ryzeAuth = new
            {
                auth.Authority,
                auth.ApiBaseUrl,
                auth.RequiredApiKeyScope,
                auth.ApiKeyIntrospectionEnabled
            },
            hubModules = PlatformModuleCatalog.EnabledModules
        });

        return Task.FromResult(errors.Count == 0 ? ExitCodes.Success : ExitCodes.Failure);
    }

    private static T Options<T>(IServiceProvider provider) where T : class =>
        provider.GetRequiredService<IOptions<T>>().Value;

    private static void Require(List<string> errors, string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required");
        }
    }
}
