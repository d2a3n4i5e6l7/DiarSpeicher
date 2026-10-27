namespace DiarSpeicher.Client.Api;

public sealed class UsersApi(HttpClient http, GatewayOptions gateway)
{
    private readonly ApiClient _client = new(http, gateway.AuthBase);

    public Task<List<UserItem>?> ListAsync() => _client.GetAsync("/users", ClientJsonContext.Default.ListUserItem);

    public Task<UserItem?> CreateAsync(CreateUserRequest request) =>
        _client.PostAsync("/users", request, ClientJsonContext.Default.CreateUserRequest, ClientJsonContext.Default.UserItem);

    public Task<UpdatedResult?> UpdateAsync(int id, UpdateUserRequest request) =>
        _client.PutAsync($"/users/{id}", request, ClientJsonContext.Default.UpdateUserRequest, ClientJsonContext.Default.UpdatedResult);

    public Task<DeletedResult?> DeleteAsync(int id) => _client.DeleteAsync($"/users/{id}", ClientJsonContext.Default.DeletedResult);

    public Task<UpdatedResult?> ChangePasswordAsync(int id, string newPassword) =>
        UpdateAsync(id, new UpdateUserRequest { NewPassword = newPassword });

    public Task<ProtocolCatalog?> ProtocolsAsync() => _client.GetAsync("/protocols", ClientJsonContext.Default.ProtocolCatalog);
}
