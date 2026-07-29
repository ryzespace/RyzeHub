using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace RyzeHub.Application.Hub;

public sealed record GitHubRepo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = string.Empty;

    [JsonPropertyName("clone_url")]
    public string CloneUrl { get; init; } = string.Empty;

    [JsonPropertyName("ssh_url")]
    public string SshUrl { get; init; } = string.Empty;

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; init; } = "main";

    [JsonPropertyName("private")]
    public bool Private { get; init; }

    [JsonPropertyName("archived")]
    public bool Archived { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }
}

public interface IGitHubManager
{
    Task<IReadOnlyList<GitHubRepo>> GetRepositoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRepositoryNamesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<(string Name, string Path)>> CloneAllRepositoriesAsync(string targetDirectory, CancellationToken cancellationToken);

    Task<string> CloneRepositoryAsync(GitHubRepo repository, string targetDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Reads organization repositories from the GitHub REST API and clones them locally.
/// </summary>
public sealed class GitHubManager(
    HttpClient httpClient,
    ILogger<GitHubManager> logger,
    string organizationName) : IGitHubManager
{
    public async Task<IReadOnlyList<GitHubRepo>> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepo>();
        var page = 1;
        const int PerPage = 100;

        while (true)
        {
            var requestUri = $"orgs/{organizationName}/repos?per_page={PerPage}&page={page}";
            logger.LogDebug("Fetching repos from: {RequestUri}", requestUri);

            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("GitHub API error: {StatusCode} - {Body}", response.StatusCode, body);
                break;
            }

            var pageRepos = await response.Content.ReadFromJsonAsync<List<GitHubRepo>>(cancellationToken);
            if (pageRepos is null || pageRepos.Count == 0)
            {
                break;
            }

            repositories.AddRange(pageRepos);
            page++;
        }

        logger.LogInformation("Fetched {Count} repositories from {Organization}", repositories.Count, organizationName);
        return repositories;
    }

    public async Task<IReadOnlyList<string>> GetRepositoryNamesAsync(CancellationToken cancellationToken) =>
        [.. (await GetRepositoriesAsync(cancellationToken)).Select(repository => repository.Name)];

    public async Task<string> CloneRepositoryAsync(
        GitHubRepo repository,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var repositoryPath = Path.Combine(targetDirectory, repository.Name);

        if (Directory.Exists(repositoryPath))
        {
            logger.LogDebug("Repository {Repository} already exists", repository.Name);
            return repositoryPath;
        }

        logger.LogInformation("Cloning {Repository}", repository.Name);

        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("clone");
        startInfo.ArgumentList.Add("--depth");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add(repository.CloneUrl);
        startInfo.ArgumentList.Add(repositoryPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to clone {repository.Name}: {stderr}");
        }

        logger.LogInformation("Cloned {Repository}", repository.Name);
        return repositoryPath;
    }

    public async Task<IReadOnlyList<(string Name, string Path)>> CloneAllRepositoriesAsync(
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var repositories = await GetRepositoriesAsync(cancellationToken);
        var cloned = new List<(string Name, string Path)>();

        Directory.CreateDirectory(targetDirectory);

        foreach (var repository in repositories)
        {
            if (repository.Archived)
            {
                logger.LogDebug("Skipping archived repo: {Repository}", repository.Name);
                continue;
            }

            try
            {
                var path = await CloneRepositoryAsync(repository, targetDirectory, cancellationToken);
                cloned.Add((repository.Name, path));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Failed to clone {Repository}", repository.Name);
            }
        }

        logger.LogInformation("Cloned {ClonedCount}/{TotalCount} repositories", cloned.Count, repositories.Count);
        return cloned;
    }
}
