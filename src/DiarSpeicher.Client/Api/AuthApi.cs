namespace DiarSpeicher.Client.Api;

public sealed class AuthApi(HttpClient http, GatewayOptions gateway)
{
    private readonly ApiClient _client = new(http, gateway.AuthBase);

    public Task<LoginResponse?> LoginAsync(LoginRequest request) =>
        _client.PostAsync("/login", request, ClientJsonContext.Default.LoginRequest, ClientJsonContext.Default.LoginResponse);

    public Task<LoginResponse?> RegisterFirstAdminAsync(LoginRequest request) =>
        _client.PostAsync("/register-first-admin", request, ClientJsonContext.Default.LoginRequest, ClientJsonContext.Default.LoginResponse);

    public Task<MeResponse?> MeAsync() => _client.GetAsync("/me", ClientJsonContext.Default.MeResponse);

    public Task<SuccessResult?> LogoutAsync() => _client.PostAsync("/logout", ClientJsonContext.Default.SuccessResult);
}
