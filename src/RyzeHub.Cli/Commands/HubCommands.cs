using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RyzeHub.Application.Hub;

namespace RyzeHub.Cli.Commands;

internal sealed class HubCommand : ICommandHandler
{
    public string Name => "hub";

    public string Usage => "hub [--dockerize]";

    public string Description => "Clone the org, analyse dependencies, compute build order";

    public async Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await provider.GetRequiredService<IHubManager>()
            .UpdateHubAsync(arguments.HasFlag("dockerize"), cancellationToken);

        ConsoleOutput.WriteJson(result);
        return result.IsSuccess ? ExitCodes.Success : ExitCodes.Failure;
    }
}

internal sealed class DepsCommand : ICommandHandler
{
    public string Name => "deps";

    public string Usage => "deps --path <p> [--repos a,b]";

    public string Description => "Report internal dependencies of a repository";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var path = arguments.GetValue("path") ?? ".";
        var repos = arguments.GetValues("repos") is { Count: > 0 } explicitRepos
            ? explicitRepos
            : HubCatalog.TrackedRepositoryNames();

        var manager = new DependencyManager(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<DependencyManager>(),
            repos);

        ConsoleOutput.WriteJson(new { path, dependencies = manager.FindInternalDependencies(path) });
        return Task.FromResult(ExitCodes.Success);
    }
}

internal sealed class DockerCommand : ICommandHandler
{
    public string Name => "docker";

    public string Usage => "docker --path <p>";

    public string Description => "Detect the language and generate a Dockerfile";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var path = arguments.GetValue("path") ?? ".";
        var manager = new DockerManager(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<DockerManager>(),
            path);

        var language = manager.DetectLanguage(path);
        var success = language == "dotnet"
            ? manager.GenerateDotnetDockerfile(path)
            : manager.GenerateDockerfile(path, language);

        ConsoleOutput.WriteJson(new { path, language, success });
        return Task.FromResult(success ? ExitCodes.Success : ExitCodes.Failure);
    }
}
