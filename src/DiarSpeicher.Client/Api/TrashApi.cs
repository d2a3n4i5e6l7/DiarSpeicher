using DiarSpeicher.Core.Domain.Catalog;

namespace DiarSpeicher.Client.Api;

public sealed class TrashApi(HttpClient http, GatewayOptions gateway)
{
    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<List<TrashEntryDto>?> ListAsync() => _client.GetAsync("/api/v2/trash/", CatalogJsonContext.Default.ListTrashEntryDto);

    public Task RestoreAsync(string id) => _client.SendAsync(HttpMethod.Post, $"/api/v2/trash/{id}/restore");

    public Task PurgeAsync(string id) => _client.SendAsync(HttpMethod.Delete, $"/api/v2/trash/{id}");
}
