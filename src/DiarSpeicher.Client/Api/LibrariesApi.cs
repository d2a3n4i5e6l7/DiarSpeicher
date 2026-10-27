using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components.Forms;

namespace DiarSpeicher.Client.Api;

public sealed class LibrariesApi(HttpClient http, GatewayOptions gateway)
{
    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<DiarSpeicherScanStatusDto?> ScanStatusAsync(string id, CancellationToken cancellationToken = default) =>
        _client.GetAsync($"/api/v2/libraries/{id}/scan", CatalogJsonContext.Default.DiarSpeicherScanStatusDto, cancellationToken);

    public Task<DiarSpeicherMissingReportDto?> MissingAsync(string id) =>
        _client.GetAsync($"/api/v2/libraries/{id}/missing", CatalogJsonContext.Default.DiarSpeicherMissingReportDto);

    public Task<RemovedResult?> PurgeMissingAsync(string id) =>
        _client.PostAsync($"/api/v2/libraries/{id}/purge-missing", CatalogJsonContext.Default.RemovedResult);

    public Task<List<DiarSpeicherScanStatusDto>?> ActiveScansAsync() =>
        _client.GetAsync("/api/v2/libraries/scans", CatalogJsonContext.Default.ListDiarSpeicherScanStatusDto);

    public string ScanStreamUrl(string id) => _client.Url($"/api/v2/libraries/{id}/scan/stream");

    public Task<List<DiarSpeicherLibraryDto>?> ListAsync(CancellationToken cancellationToken = default) =>
        _client.GetAsync("/api/v2/libraries", CatalogJsonContext.Default.ListDiarSpeicherLibraryDto, cancellationToken);

    public Task<DiarSpeicherLibraryDto?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _client.GetAsync($"/api/v2/libraries/{id}", CatalogJsonContext.Default.DiarSpeicherLibraryDto, cancellationToken);

    public Task<DiarSpeicherLibraryDto?> CreateAsync(DiarSpeicherCreateLibraryInput payload) =>
        _client.PostAsync("/api/v2/libraries", payload,
            CatalogJsonContext.Default.DiarSpeicherCreateLibraryInput, CatalogJsonContext.Default.DiarSpeicherLibraryDto);

    public Task<DiarSpeicherLibraryDto?> UpdateAsync(string id, DiarSpeicherUpdateLibraryInput payload) =>
        _client.PutAsync($"/api/v2/libraries/{id}", payload,
            CatalogJsonContext.Default.DiarSpeicherUpdateLibraryInput, CatalogJsonContext.Default.DiarSpeicherLibraryDto);

    public Task DeleteAsync(string id, bool deleteFiles = false) =>
        _client.SendAsync(HttpMethod.Delete, $"/api/v2/libraries/{id}?deleteFiles={MediaApi.Flag(deleteFiles)}");

    public Task ScanAsync(string id) => _client.SendAsync(HttpMethod.Post, $"/api/v2/libraries/{id}/scan");

    public async Task<DiarSpeicherUploadResponseDto?> UploadAsync(string libraryId, IReadOnlyList<IBrowserFile> files, string? subpath, long maxFileBytes)
    {
        using var form = new MultipartFormDataContent();
        if (!string.IsNullOrEmpty(subpath))
        {
            form.Add(new StringContent(subpath), "subpath");
        }

        foreach (var file in files)
        {
            form.Add(new StreamContent(file.OpenReadStream(maxFileBytes)), "files", file.Name);
        }

        return await _client.SendAsync(HttpMethod.Post, $"/api/v2/libraries/{libraryId}/upload", form,
            CatalogJsonContext.Default.DiarSpeicherUploadResponseDto);
    }

    public Task<SystemClaimResponse?> ClaimStatusAsync() =>
        _client.GetAsync("/api/v2/claim", CatalogJsonContext.Default.SystemClaimResponse);
}
