using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components.Forms;

namespace DiarSpeicher.Client.Api;

public sealed class SeriesApi(HttpClient http, GatewayOptions gateway)
{
    public const long MaxCoverBytes = 20L * 1024 * 1024;

    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<DiarSpeicherPageResponse<DiarSpeicherSeriesDto>?> ListAsync(
        string? libraryId, int page = 0, int pageSize = 24, CancellationToken cancellationToken = default)
    {
        var scope = string.IsNullOrEmpty(libraryId) ? string.Empty : $"&libraryId={Uri.EscapeDataString(libraryId)}";
        return _client.GetAsync($"/api/v2/series?{MediaApi.PageQuery(page, pageSize)}{scope}",
            CatalogJsonContext.Default.DiarSpeicherPageResponseDiarSpeicherSeriesDto, cancellationToken);
    }

    public Task<DiarSpeicherSeriesDto?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _client.GetAsync($"/api/v2/series/{id}", CatalogJsonContext.Default.DiarSpeicherSeriesDto, cancellationToken);

    public Task DeleteAsync(string id, bool deleteFiles = false) =>
        _client.SendAsync(HttpMethod.Delete, $"/api/v2/series/{id}?deleteFiles={MediaApi.Flag(deleteFiles)}");

    public Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>?> MediaAsync(
        string id, int page = 0, int pageSize = 50, CancellationToken cancellationToken = default) =>
        _client.GetAsync($"/api/v2/series/{id}/media?{MediaApi.PageQuery(page, pageSize)}",
            CatalogJsonContext.Default.DiarSpeicherPageResponseDiarSpeicherMediaDto, cancellationToken);

    public Task<DiarSpeicherSeriesDto?> UpdateAsync(string id, DiarSpeicherUpdateSeriesInput payload) =>
        _client.PutAsync($"/api/v2/series/{id}", payload,
            CatalogJsonContext.Default.DiarSpeicherUpdateSeriesInput, CatalogJsonContext.Default.DiarSpeicherSeriesDto);

    public string ThumbnailUrl(string id, string? token = null)
    {
        var query = string.IsNullOrEmpty(token) ? string.Empty : $"?v={Uri.EscapeDataString(token)}";
        return _client.Url($"/api/v2/series/{id}/thumbnail{query}");
    }

    public Task<UpdatedResult?> SetCoverFromMediaAsync(string seriesId, string mediaId) =>
        _client.PutAsync($"/api/v2/series/{seriesId}/thumbnail", new DiarSpeicherSeriesThumbnailInput { MediaId = mediaId },
            CatalogJsonContext.Default.DiarSpeicherSeriesThumbnailInput, CatalogJsonContext.Default.UpdatedResult);

    public async Task<UpdatedResult?> UploadCoverAsync(string seriesId, IBrowserFile file)
    {
        await using var stream = file.OpenReadStream(MaxCoverBytes);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(stream), "file", file.Name);
        return await _client.SendAsync(HttpMethod.Post, $"/api/v2/series/{seriesId}/thumbnail", form,
            CatalogJsonContext.Default.UpdatedResult);
    }

    public Task ClearCoverAsync(string seriesId) => _client.SendAsync(HttpMethod.Delete, $"/api/v2/series/{seriesId}/thumbnail");
}
