using System.Runtime.InteropServices.JavaScript;
using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Interop;

namespace DiarSpeicher.Client.Auth;

public sealed class SessionState : IDisposable
{
    private readonly AuthApi _auth;
    private readonly UnauthorizedSignal _unauthorized;

    public SessionState(AuthApi auth, UnauthorizedSignal unauthorized)
    {
        _auth = auth;
        _unauthorized = unauthorized;
        _unauthorized.Raised += OnUnauthorized;
    }

    public event Action? Changed;

    public SessionUser? User { get; private set; }

    public bool Loading { get; private set; } = true;

    public async Task InitializeAsync()
    {
        await RefreshUserAsync();
        Loading = false;
        Changed?.Invoke();
    }

    public async Task RefreshUserAsync()
    {
        try
        {
            var me = await _auth.MeAsync();
            User = me?.User;
        }
        catch (ApiException)
        {
            User = null;
        }

        Changed?.Invoke();
    }

    public async Task<LoginResponse?> LoginAsync(string username, string password)
    {
        var publicKey = await TryGetPublicKeyAsync();
        var response = await _auth.LoginAsync(new LoginRequest(username, password, publicKey));
        if (response?.RequiresFirstAdmin == true)
        {
            return response;
        }

        await AdoptAsync(response);
        return response;
    }

    public async Task RegisterFirstAdminAsync(string username, string password)
    {
        var publicKey = await TryGetPublicKeyAsync();
        var response = await _auth.RegisterFirstAdminAsync(new LoginRequest(username, password, publicKey));
        await AdoptAsync(response);
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _auth.LogoutAsync();
        }
        catch (ApiException)
        {
        }
        finally
        {
            await TryResetKeyPairAsync();
            User = null;
            Changed?.Invoke();
        }
    }

    public void Dispose() => _unauthorized.Raised -= OnUnauthorized;

    private static async Task<string?> TryGetPublicKeyAsync()
    {
        try
        {
            return await DpopJs.GetPublicKeyBase64();
        }
        catch (JSException)
        {
            return null;
        }
    }

    private static async Task TryResetKeyPairAsync()
    {
        try
        {
            await DpopJs.ResetKeyPair();
        }
        catch (JSException)
        {
        }
    }

    private async Task AdoptAsync(LoginResponse? response)
    {
        if (response?.User is null)
        {
            await RefreshUserAsync();
            return;
        }

        User = response.User;
        Changed?.Invoke();
    }

    private void OnUnauthorized()
    {
        User = null;
        Changed?.Invoke();
    }
}
