namespace DiarSpeicher.Core.Domain.Models;

public class AuthUser
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public bool IsServerOwner { get; set; }
    public HashSet<string> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExcludedLibraryIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int? AgeRestriction { get; set; }
    public bool RestrictOnUnset { get; set; } = true;

    public bool HasRole(string role) =>
        IsServerOwner || Roles.Contains(role);

    /// <summary>
    /// The server owner is exempt, as in <see cref="HasRole"/>: it is the only local privilege
    /// gate and is granted before anyone exists who could hand out permissions. The Gateway
    /// also recognizes the "*" wildcard.
    /// </summary>
    public bool HasPermission(string permission) =>
        IsServerOwner || Permissions.Contains(PermissionWildcard) || Permissions.Contains(permission);

    /// <summary>Value the Gateway interprets as "all permissions".</summary>
    public const string PermissionWildcard = "*";

    /// <summary>
    /// True for a request the gateway let through on a public route, which carries no identity.
    /// </summary>
    public bool IsAnonymous => string.IsNullOrEmpty(Id);

    /// <summary>
    /// Identity for a public route: no id, no roles, not the owner, and age-restricted on
    /// unset, so an anonymous reader can never reach more than a named one. It is never
    /// mirrored into the database.
    /// </summary>
    public static AuthUser CreateAnonymous() => new()
    {
        Id = string.Empty,
        Username = "anonymous",
        RestrictOnUnset = true
    };
}
