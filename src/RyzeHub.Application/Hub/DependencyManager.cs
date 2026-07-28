using Microsoft.Extensions.Logging;

namespace RyzeHub.Application.Hub;

public sealed class DependencyGraph
{
    private readonly Dictionary<string, List<string>> _adjacency = new(StringComparer.Ordinal);
    private readonly List<(string From, string To)> _edges = [];
    private readonly HashSet<string> _repositories = new(StringComparer.Ordinal);

    public IReadOnlySet<string> Repositories => _repositories;

    public IReadOnlyList<(string From, string To)> Edges => _edges;

    public void AddRepository(string name, IReadOnlyList<string> dependencies)
    {
        _repositories.Add(name);
        _adjacency[name] = [.. dependencies];

        foreach (var dependency in dependencies)
        {
            _edges.Add((name, dependency));
        }
    }

    /// <summary>Kahn topological sort; returns null when a dependency cycle exists.</summary>
    public IReadOnlyList<string>? TopologicalSort()
    {
        var inDegree = _repositories.ToDictionary(repository => repository, static _ => 0, StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (from, to) in _edges)
        {
            inDegree[from] = inDegree.GetValueOrDefault(from) + 1;

            if (!dependents.TryGetValue(to, out var list))
            {
                list = [];
                dependents[to] = list;
            }

            list.Add(from);
        }

        var queue = new Stack<string>(inDegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var result = new List<string>();

        while (queue.Count > 0)
        {
            var repository = queue.Pop();
            result.Add(repository);

            if (!dependents.TryGetValue(repository, out var next))
            {
                continue;
            }

            foreach (var dependent in next)
            {
                if (!inDegree.TryGetValue(dependent, out var degree))
                {
                    continue;
                }

                inDegree[dependent] = degree - 1;
                if (degree - 1 == 0)
                {
                    queue.Push(dependent);
                }
            }
        }

        return result.Count == _repositories.Count ? result : null;
    }

    public IReadOnlyList<string> GetDependents(string repositoryName) =>
        [.. _edges.Where(edge => edge.To == repositoryName).Select(edge => edge.From)];
}

/// <summary>
/// Scans repositories for references to other repositories inside the organization.
/// </summary>
public sealed class DependencyManager
{
    private static readonly string[] ManifestFiles =
    [
        "requirements.txt",
        "package.json",
        "go.mod",
        "pyproject.toml",
        "Cargo.toml",
        "pom.xml",
        "build.gradle",
        "Directory.Packages.props"
    ];

    private readonly ILogger<DependencyManager> _logger;
    private readonly List<string> _repositoryNames = [];

    public DependencyManager(ILogger<DependencyManager> logger, IEnumerable<string> organizationRepositoryNames)
    {
        _logger = logger;

        foreach (var repositoryName in organizationRepositoryNames.Concat(HubCatalog.TrackedRepositoryNames()))
        {
            var canonical = HubCatalog.ResolveCanonicalRepositoryName(repositoryName) ?? repositoryName;
            if (!_repositoryNames.Contains(canonical, StringComparer.Ordinal))
            {
                _repositoryNames.Add(canonical);
            }
        }
    }

    public IReadOnlyList<string> FindInternalDependencies(string repositoryPath)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var currentRepositoryName = HubCatalog.ResolveCanonicalRepositoryName(
            Path.GetFileName(repositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty);

        // Project files and shared protobuf contracts also express cross-repository dependencies.
        var candidateFiles = ManifestFiles
            .Select(fileName => Path.Combine(repositoryPath, fileName))
            .Where(File.Exists)
            .Concat(SafeEnumerateFiles(repositoryPath, "*.csproj"))
            .Concat(SafeEnumerateFiles(repositoryPath, "*.proto"));

        foreach (var filePath in candidateFiles)
        {
            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Failed to read {FilePath}", filePath);
                continue;
            }

            foreach (var repositoryName in _repositoryNames)
            {
                if (!ContainsDependency(content, repositoryName))
                {
                    continue;
                }

                if (currentRepositoryName != repositoryName)
                {
                    dependencies.Add(repositoryName);
                }

                _logger.LogDebug("Found dependency {Repository} in {FilePath}", repositoryName, filePath);
            }
        }

        var result = dependencies.Order(StringComparer.Ordinal).ToArray();
        if (result.Length > 0)
        {
            _logger.LogInformation(
                "Found {Count} internal dependencies in {RepositoryPath}",
                result.Length,
                repositoryPath);
        }

        return result;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string repositoryPath, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(repositoryPath, pattern, SearchOption.AllDirectories).Take(200);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool ContainsDependency(string content, string repositoryName)
    {
        var normalizedContent = HubCatalog.NormalizeRepositoryToken(content);

        foreach (var identifier in HubCatalog.DependencyIdentifiers(repositoryName))
        {
            if (content.Contains(identifier, StringComparison.Ordinal))
            {
                return true;
            }

            var normalizedIdentifier = HubCatalog.NormalizeRepositoryToken(identifier);
            if (normalizedIdentifier.Length != 0 && normalizedContent.Contains(normalizedIdentifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public DependencyGraph BuildDependencyGraph(IReadOnlyList<(string Name, string Path)> repositories)
    {
        var graph = new DependencyGraph();

        foreach (var (name, path) in repositories)
        {
            var canonical = HubCatalog.ResolveCanonicalRepositoryName(name) ?? name;
            graph.AddRepository(canonical, FindInternalDependencies(path));
        }

        _logger.LogInformation(
            "Built dependency graph: {Repositories} repositories, {Edges} edges",
            graph.Repositories.Count,
            graph.Edges.Count);

        return graph;
    }
}
