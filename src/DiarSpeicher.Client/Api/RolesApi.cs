namespace DiarSpeicher.Client.Api;

public sealed class RolesApi
{
    private static readonly IReadOnlyList<RoleItem> Roles =
    [
        new(1, "admin", "Administrador del reino", IsAdmin: true, UserCount: 1),
        new(2, "reader", "Lector estándar", IsAdmin: false, UserCount: 0),
    ];

    public Task<IReadOnlyList<RoleItem>> ListAsync() => Task.FromResult(Roles);

    public Task CreateAsync(string name, string? description, bool isAdmin) => Task.CompletedTask;

    public Task UpdateAsync(int id, string name, string? description, bool isAdmin) => Task.CompletedTask;

    public Task DeleteAsync(int id) =>
        id > 0 ? Task.CompletedTask : Task.FromException(new ApiException(0, "ID inválido"));
}
