using System.Globalization;
using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Auth;
using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Client.Components.Users;

public sealed class UserFormState
{
    public const string DefaultRole = "reader";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = DefaultRole;

    public bool IsEnabled { get; set; } = true;

    public string Age { get; set; } = string.Empty;

    public bool Wildcard { get; set; }

    public List<string> Permissions { get; private init; } = [];

    public List<string> Protocols { get; private init; } = [];

    public bool HasAge => Age.Trim().Length > 0;

    public static UserFormState Empty() => new()
    {
        Permissions = PermissionService.ParseCsv(PermissionService.DefaultPermissions),
        Protocols = [PermissionService.ProtocolApiKey],
    };

    public static UserFormState FromUser(UserItem user)
    {
        var entries = PermissionService.ParseCsv(user.Permissions);
        return new UserFormState
        {
            Username = user.Username,
            Role = user.Role ?? DefaultRole,
            IsEnabled = user.IsEnabled,
            Age = user.AgeRestriction?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Wildcard = entries.Contains(AuthUser.PermissionWildcard),
            Permissions = entries.FindAll(entry => entry != AuthUser.PermissionWildcard),
            Protocols = PermissionService.ParseCsv(user.AllowedProtocols),
        };
    }

    public int? ParseAge() =>
        int.TryParse(Age.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var age) ? age : null;

    public string SerializePermissions() => Wildcard ? AuthUser.PermissionWildcard : string.Join(',', Permissions);

    public string SerializeProtocols() =>
        Protocols.Count > 0 ? string.Join(',', Protocols) : PermissionService.ProtocolApiKey;

    public void SetPermission(string id, bool enabled) => Toggle(Permissions, id, enabled);

    public void SetProtocol(string id, bool enabled) => Toggle(Protocols, id, enabled);

    private static void Toggle(List<string> list, string id, bool enabled)
    {
        if (!enabled)
        {
            list.Remove(id);
        }
        else if (!list.Contains(id))
        {
            list.Add(id);
        }
    }
}
