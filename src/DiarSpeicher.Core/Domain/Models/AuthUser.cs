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
}
