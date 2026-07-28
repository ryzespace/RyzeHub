using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Infrastructure.RyzeAuth;

public interface IRyzeAuthRoleSynchronizer
{
    /// <summary>Projects RyzeAuth realm roles and scopes onto hub platform roles and permissions.</summary>
    void Synchronize(ClaimsPrincipal principal);

    IReadOnlyList<string> MapRoles(IEnumerable<string> realmRoles);
}

public sealed class RyzeAuthRoleSynchronizer(
    IHubPlatform hub,
    ILogger<RyzeAuthRoleSynchronizer> logger,
    IOptions<RyzeAuthOptions> options) : IRyzeAuthRoleSynchronizer
{
    private readonly RyzeAuthOptions _options = options.Value;

    public void Synchronize(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return;
        }

        var realmRoles = principal.FindAll("roles").Select(claim => claim.Value)
            .Concat(principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var hubRole in MapRoles(realmRoles))
        {
            hub.AssignRole(subject, hubRole);
        }

        // Scopes of the form "hub:<resource>:<action>" become granular hub permissions.
        foreach (var scope in principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(scope => scope.StartsWith("hub:", StringComparison.Ordinal)))
        {
            hub.GrantPermission(subject, scope["hub:".Length..]);
        }

        logger.LogDebug("Synchronized RyzeAuth claims for subject {Subject}", subject);
    }

    public IReadOnlyList<string> MapRoles(IEnumerable<string> realmRoles)
    {
        var mapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var realmRole in realmRoles)
        {
            if (_options.RoleMappings.TryGetValue(realmRole, out var hubRole))
            {
                mapped.Add(hubRole);
            }
        }

        return [.. mapped.Order(StringComparer.Ordinal)];
    }
}
