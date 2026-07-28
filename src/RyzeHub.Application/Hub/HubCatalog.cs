namespace RyzeHub.Application.Hub;

public enum RepositoryStage
{
    Active,
    Future
}

public sealed record RepositoryDefinition(string CanonicalName, RepositoryStage Stage, IReadOnlyList<string> Aliases);

/// <summary>
/// Repositories managed by the hub plus the planned dependency targets.
/// </summary>
public static class HubCatalog
{
    public static IReadOnlyList<RepositoryDefinition> Repositories { get; } =
    [
        new("RyzeAuth", RepositoryStage.Active,
            ["ryzeauth", "ryze-auth", "ryzespace/ryzeauth", "@ryzespace/auth", "RyzeSpace.Auth"]),
        new("RyzeSpace.Client", RepositoryStage.Active,
            ["client", "ryzespace-client", "ryzespace_client", "ryzespace/client", "@ryzespace/client"]),
        new("RyzeSpace.HelpCenter", RepositoryStage.Active,
            ["helpcenter", "help-center", "ryzespace-helpcenter", "ryzespace_helpcenter", "ryzespace/helpcenter", "@ryzespace/helpcenter"]),
        new("RyzeSpace.AdminPanel", RepositoryStage.Future,
            ["admin", "admin-panel", "panel-admina", "ryzespace-adminpanel", "ryzespace/admin-panel", "@ryzespace/admin-panel"]),
        new("RyzeSpace.Mobile", RepositoryStage.Future,
            ["mobile", "app-mobile", "aplikacja-mobilna", "ryzespace-mobile", "ryzespace/mobile", "@ryzespace/mobile"]),
        new("RyzeSpace.Desktop", RepositoryStage.Future,
            ["desktop", "desktop-app", "aplikacja-desktopowa", "ryzespace-desktop", "ryzespace/desktop", "@ryzespace/desktop"])
    ];

    public static IReadOnlyList<string> ActiveRepositoryNames() => NamesByStage(RepositoryStage.Active);

    public static IReadOnlyList<string> FutureRepositoryNames() => NamesByStage(RepositoryStage.Future);

    public static IReadOnlyList<string> TrackedRepositoryNames() =>
        [.. Repositories.Select(repository => repository.CanonicalName)];

    public static IReadOnlyList<string> NamesByStage(RepositoryStage stage) =>
        [.. Repositories.Where(repository => repository.Stage == stage).Select(repository => repository.CanonicalName)];

    public static string? ResolveCanonicalRepositoryName(string value)
    {
        var normalized = NormalizeRepositoryToken(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        return Repositories
            .FirstOrDefault(repository => RepositoryTokens(repository).Contains(normalized))
            ?.CanonicalName;
    }

    public static IReadOnlyList<string> DependencyIdentifiers(string value)
    {
        var definition = FindRepositoryDefinition(value);
        if (definition is null)
        {
            return DedupePreserveOrder(DerivedIdentifiers(value));
        }

        var identifiers = new List<string> { definition.CanonicalName };
        identifiers.AddRange(definition.Aliases);
        identifiers.AddRange(DerivedIdentifiers(definition.CanonicalName));
        return DedupePreserveOrder(identifiers);
    }

    public static string NormalizeRepositoryToken(string value) =>
        new([.. value.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant)]);

    private static RepositoryDefinition? FindRepositoryDefinition(string value)
    {
        var normalized = NormalizeRepositoryToken(value);
        return Repositories.FirstOrDefault(repository => RepositoryTokens(repository).Contains(normalized));
    }

    private static HashSet<string> RepositoryTokens(RepositoryDefinition repository)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeRepositoryToken(repository.CanonicalName)
        };

        foreach (var alias in repository.Aliases)
        {
            tokens.Add(NormalizeRepositoryToken(alias));
        }

        foreach (var identifier in DerivedIdentifiers(repository.CanonicalName))
        {
            tokens.Add(NormalizeRepositoryToken(identifier));
        }

        return tokens;
    }

    private static List<string> DerivedIdentifiers(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var identifiers = new List<string> { trimmed, trimmed.ToLowerInvariant() };
        var parts = trimmed.Split(['.', '-', '_', '/', '@'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            identifiers.Add(parts[^1]);
            identifiers.Add(parts[^1].ToLowerInvariant());
        }

        if (parts.Length >= 2)
        {
            var namespacePart = string.Concat(parts[..^1]);
            var namespaceKebab = string.Join('-', parts[..^1].Select(part => part.ToLowerInvariant()));
            var name = parts[^1].ToLowerInvariant();

            identifiers.Add($"{namespacePart}.{name}");
            identifiers.Add($"{namespacePart}-{name}");
            identifiers.Add($"{namespacePart}_{name}");
            identifiers.Add($"{namespacePart}/{name}");
            identifiers.Add($"@{namespaceKebab}/{name}");
        }

        return DedupePreserveOrder(identifiers);
    }

    private static List<string> DedupePreserveOrder(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (seen.Add(NormalizeRepositoryToken(value)))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
