using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IAccessControlStore
{
    void AssignRole(string userId, string roleName);

    void GrantPermission(string userId, string permission);

    IReadOnlyList<string> PermissionsForUser(string userId);

    bool HasPermission(string userId, string permission);

    UserAccessProfile AccessProfile(string userId);

    IReadOnlyList<RoleDefinition> Roles();
}

/// <summary>
/// Permission and Role Hub: business roles plus granular permissions.
/// Populated from RyzeAuth realm roles and <c>hub:*</c> scopes.
/// </summary>
public sealed class AccessControlStore : IAccessControlStore
{
    private readonly Dictionary<string, RoleDefinition> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _userRoles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _directPermissions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public AccessControlStore()
    {
        foreach (var role in PlatformModuleCatalog.DefaultRoles)
        {
            _roles[role.Name] = role;
        }
    }

    public void AssignRole(string userId, string roleName)
    {
        lock (_gate)
        {
            if (!_userRoles.TryGetValue(userId, out var roles))
            {
                roles = new HashSet<string>(StringComparer.Ordinal);
                _userRoles[userId] = roles;
            }

            roles.Add(roleName);
        }
    }

    public void GrantPermission(string userId, string permission)
    {
        lock (_gate)
        {
            if (!_directPermissions.TryGetValue(userId, out var permissions))
            {
                permissions = new HashSet<string>(StringComparer.Ordinal);
                _directPermissions[userId] = permissions;
            }

            permissions.Add(permission);
        }
    }

    public IReadOnlyList<string> PermissionsForUser(string userId)
    {
        lock (_gate)
        {
            return Resolve(userId);
        }
    }

    public bool HasPermission(string userId, string permission)
    {
        lock (_gate)
        {
            return Resolve(userId).Contains(permission, StringComparer.Ordinal);
        }
    }

    public UserAccessProfile AccessProfile(string userId)
    {
        lock (_gate)
        {
            string[] roles = _userRoles.TryGetValue(userId, out var assigned)
                ? [.. assigned.Order(StringComparer.Ordinal)]
                : [];

            string[] direct = _directPermissions.TryGetValue(userId, out var permissions)
                ? [.. permissions.Order(StringComparer.Ordinal)]
                : [];

            return new UserAccessProfile(userId, roles, direct, Resolve(userId));
        }
    }

    public IReadOnlyList<RoleDefinition> Roles()
    {
        lock (_gate)
        {
            return [.. _roles.Values.OrderBy(role => role.Name, StringComparer.Ordinal)];
        }
    }

    /// <summary>Union of role-derived and directly granted permissions. Caller must hold the lock.</summary>
    private string[] Resolve(string userId)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);

        if (_userRoles.TryGetValue(userId, out var assignedRoles))
        {
            foreach (var roleName in assignedRoles)
            {
                if (_roles.TryGetValue(roleName, out var role))
                {
                    foreach (var permission in role.Permissions)
                    {
                        permissions.Add(permission);
                    }
                }
            }
        }

        if (_directPermissions.TryGetValue(userId, out var direct))
        {
            foreach (var permission in direct)
            {
                permissions.Add(permission);
            }
        }

        return [.. permissions.Order(StringComparer.Ordinal)];
    }
}
