using System.Globalization;
using System.Security.Claims;
using DiarSpeicher.Client.Api;
using Microsoft.AspNetCore.Components.Authorization;

namespace DiarSpeicher.Client.Auth;

public sealed class SessionAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    public const string AdminClaim = "is_admin";
    private const string AuthenticationType = "Gateway";

    private readonly SessionState _session;

    public SessionAuthenticationStateProvider(SessionState session)
    {
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(Build(_session.User));

    public void Dispose() => _session.Changed -= OnSessionChanged;

    private void OnSessionChanged() => NotifyAuthenticationStateChanged(Task.FromResult(Build(_session.User)));

    private static AuthenticationState Build(SessionUser? user)
    {
        if (user is null)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(AdminClaim, user.IsAdmin ? "true" : "false"),
        ];
        if (!string.IsNullOrEmpty(user.Role))
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)));
    }
}
