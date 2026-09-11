namespace DiarSpeicher.Core.Domain.Models;

public class AuthUser
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public bool IsServerOwner { get; set; }
    public HashSet<string> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Permisos tal y como llegan en <c>X-Auth-Perms</c>. El catálogo de nombres válidos
    /// está en <see cref="DiarSpeicher.Core.Domain.Models.Permissions"/>.
    /// </summary>
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExcludedLibraryIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int? AgeRestriction { get; set; }
    public bool RestrictOnUnset { get; set; } = true;

    public bool HasRole(string role) =>
        IsServerOwner || Roles.Contains(role);

    /// <summary>
    /// El propietario del servidor queda exento, igual que en <see cref="HasRole"/>: es la
    /// única puerta de privilegio local y se concede antes de que exista nadie que pueda
    /// repartir permisos. El comodín "*" lo reconoce también el Gateway.
    /// </summary>
    public bool HasPermission(string permission) =>
        IsServerOwner || Permissions.Contains(PermissionWildcard) || Permissions.Contains(permission);

    /// <summary>Valor que el Gateway interpreta como "todos los permisos".</summary>
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
