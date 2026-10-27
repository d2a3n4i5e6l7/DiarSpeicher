using DiarSpeicher.Core.Domain.Catalog;

namespace DiarSpeicher.Client.Api;

public sealed class MediaApi(HttpClient http, GatewayOptions gateway)
{
    public const int MaxPageSize = 100;

    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>?> ListAsync(int page = 0, int pageSize = 20) =>
        _client.GetAsync($"/api/v2/media?{PageQuery(page, pageSize)}", CatalogJsonContext.Default.DiarSpeicherPageResponseDiarSpeicherMediaDto);

    public Task<List<DiarSpeicherMediaDto>?> KeepReadingAsync() =>
        _client.GetAsync("/api/v2/media/keep-reading", CatalogJsonContext.Default.ListDiarSpeicherMediaDto);

    public Task<DiarSpeicherMediaDto?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _client.GetAsync($"/api/v2/media/{id}", CatalogJsonContext.Default.DiarSpeicherMediaDto, cancellationToken);

    public Task DeleteAsync(string id, bool deleteFile = false) =>
        _client.SendAsync(HttpMethod.Delete, $"/api/v2/media/{id}?deleteFile={Flag(deleteFile)}");

    public Task<UpdatedResult?> UpdateProgressAsync(string id, DiarSpeicherUpdateProgressInput payload) =>
        _client.PutAsync($"/api/v2/media/{id}/progress", payload,
            CatalogJsonContext.Default.DiarSpeicherUpdateProgressInput, CatalogJsonContext.Default.UpdatedResult);

    public async Task<List<DiarSpeicherMediaDto>> LatestAsync(int limit = 20)
    {
        var page = await _client.GetAsync(
            $"/api/v2/media?{PageQuery(0, limit)}&newestFirst=true",
            CatalogJsonContext.Default.DiarSpeicherPageResponseDiarSpeicherMediaDto);
        return page?.Data ?? [];
    }

    public string ThumbnailUrl(string id) => _client.Url($"/api/v2/media/{id}/thumbnail");

    public string PageUrl(string id, int page) => _client.Url($"/api/v2/media/{id}/page/{page}");

    public string FileUrl(string id) => _client.Url($"/api/v2/media/{id}/file");

    internal static string PageQuery(int page, int pageSize) => $"page={page}&pageSize={Math.Min(pageSize, MaxPageSize)}";

    internal static string Flag(bool value) => value ? "true" : "false";
}
