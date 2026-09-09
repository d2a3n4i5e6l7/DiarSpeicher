namespace DiarSpeicher.Core.Domain.Models;

public class AuthUser
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public bool IsServerOwner { get; set; }
    public HashSet<string> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExcludedLibraryIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int? AgeRestriction { get; set; }
    public bool RestrictOnUnset { get; set; } = true;

    public bool HasRole(string role) =>
        IsServerOwner || Roles.Contains(role);

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
