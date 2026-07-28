using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform;

namespace RyzeHub.Application.Hub;

public sealed record HubUpdateResult
{
    public int ClonedCount { get; set; }
    public int DependencyEdges { get; set; }
    public int DockerizedCount { get; set; }
    public IReadOnlyList<string> BuildOrder { get; set; } = [];
    public IReadOnlyList<string> ActiveRepositories { get; set; } = [];
    public IReadOnlyList<string> MissingActiveRepositories { get; set; } = [];
    public IReadOnlyList<string> FutureDependencyTargets { get; set; } = [];
    public IReadOnlyList<string> EnabledPlatformModules { get; set; } = [];
    public List<string> Errors { get; init; } = [];

    public bool IsSuccess => Errors.Count == 0;
}

public interface IHubManager
{
    Task<HubUpdateResult> UpdateHubAsync(bool dockerize, CancellationToken cancellationToken);

    IReadOnlyDictionary<string, string> GetRepositoryPaths();
}

/// <summary>
/// Clones the organization, analyses cross-repository dependencies and prepares container builds.
/// </summary>
public sealed class HubManager(
    ILogger<HubManager> logger,
    ILoggerFactory loggerFactory,
    IGitHubManager github,
    IOptions<HubManagerOptions> options) : IHubManager
{
    private readonly HubManagerOptions _options = options.Value;

    public async Task<HubUpdateResult> UpdateHubAsync(bool dockerize, CancellationToken cancellationToken)
    {
        logger.LogInformation("Hub update started");
        var result = new HubUpdateResult();

        logger.LogInformation("Step 1: Cloning repositories");
        var clonedRepos = await github.CloneAllRepositoriesAsync(_options.HubDirectory, cancellationToken);
        result.ClonedCount = clonedRepos.Count;
        logger.LogInformation("Cloned {Count} repositories", clonedRepos.Count);

        var clonedNames = clonedRepos
            .Select(repository => HubCatalog.ResolveCanonicalRepositoryName(repository.Name) ?? repository.Name)
            .ToHashSet(StringComparer.Ordinal);

        result.ActiveRepositories = [.. HubCatalog.ActiveRepositoryNames().Where(clonedNames.Contains)];
        result.MissingActiveRepositories = [.. HubCatalog.ActiveRepositoryNames().Where(name => !clonedNames.Contains(name))];
        result.FutureDependencyTargets = HubCatalog.FutureRepositoryNames();
        result.EnabledPlatformModules = PlatformModuleCatalog.EnabledModules;

        if (result.ActiveRepositories.Count > 0)
        {
            logger.LogInformation("Active hub repositories available: {Repositories}", string.Join(", ", result.ActiveRepositories));
        }

        if (result.MissingActiveRepositories.Count > 0)
        {
            logger.LogWarning("Missing active hub repositories: {Repositories}", string.Join(", ", result.MissingActiveRepositories));
        }

        logger.LogInformation("Step 2: Analyzing dependencies");
        var repositoryNames = await github.GetRepositoryNamesAsync(cancellationToken);
        var dependencyManager = new DependencyManager(loggerFactory.CreateLogger<DependencyManager>(), repositoryNames);
        var graph = dependencyManager.BuildDependencyGraph(clonedRepos);
        result.DependencyEdges = graph.Edges.Count;
        logger.LogInformation("Found {Count} dependency edges", graph.Edges.Count);

        if (dockerize)
        {
            logger.LogInformation("Step 3: Generating Dockerfiles");
            var dockerManager = new DockerManager(loggerFactory.CreateLogger<DockerManager>(), _options.HubDirectory);
            result.DockerizedCount = dockerManager.ProcessRepositories(clonedRepos).Count;
            logger.LogInformation("Generated {Count} Dockerfiles", result.DockerizedCount);
        }

        logger.LogInformation("Step 4: Calculating build order");
        var buildOrder = graph.TopologicalSort();
        if (buildOrder is null)
        {
            logger.LogError("Dependency cycle detected");
            result.Errors.Add("Dependency cycle detected");
        }
        else
        {
            logger.LogInformation("Build order: {BuildOrder}", string.Join(" -> ", buildOrder));
            result.BuildOrder = buildOrder;
        }

        logger.LogInformation("Hub update complete");
        return result;
    }

    public IReadOnlyDictionary<string, string> GetRepositoryPaths()
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!Directory.Exists(_options.HubDirectory))
        {
            return paths;
        }

        foreach (var directory in Directory.EnumerateDirectories(_options.HubDirectory))
        {
            paths[Path.GetFileName(directory)] = directory;
        }

        return paths;
    }
}
