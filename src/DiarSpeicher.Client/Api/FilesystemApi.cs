using DiarSpeicher.Core.Domain.Catalog;

namespace DiarSpeicher.Client.Api;

public sealed class FilesystemApi(HttpClient http, GatewayOptions gateway)
{
    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<DeletionPreviewDto?> DeletionPreviewAsync(string path) =>
        _client.GetAsync($"/api/v2/filesystem/deletion?path={Uri.EscapeDataString(path)}", CatalogJsonContext.Default.DeletionPreviewDto);

    public Task<TrashEntryDto?> DeleteEntryAsync(string path) =>
        _client.DeleteAsync($"/api/v2/filesystem/entry?path={Uri.EscapeDataString(path)}", CatalogJsonContext.Default.TrashEntryDto);

    public Task<ScanPreviewDto?> PreviewAsync(string path, string pattern) =>
        _client.GetAsync($"/api/v2/filesystem/preview?path={Uri.EscapeDataString(path)}&pattern={Uri.EscapeDataString(pattern)}",
            CatalogJsonContext.Default.ScanPreviewDto);

    public Task<DiskUsageDto?> DiskAsync(string path) =>
        _client.GetAsync($"/api/v2/filesystem/disk?path={Uri.EscapeDataString(path)}", CatalogJsonContext.Default.DiskUsageDto);

    public Task<FolderEntryDto?> CreateFolderAsync(string parent, string name) =>
        _client.PostAsync("/api/v2/filesystem/folder", new CreateFolderInput { Parent = parent, Name = name },
            CatalogJsonContext.Default.CreateFolderInput, CatalogJsonContext.Default.FolderEntryDto);

    public Task<List<FolderRootDto>?> RootsAsync() =>
        _client.GetAsync("/api/v2/filesystem/roots", CatalogJsonContext.Default.ListFolderRootDto);

    public Task<FolderListingDto?> BrowseAsync(string? path = null)
    {
        var query = string.IsNullOrEmpty(path) ? string.Empty : $"?path={Uri.EscapeDataString(path)}";
        return _client.GetAsync($"/api/v2/filesystem/browse{query}", CatalogJsonContext.Default.FolderListingDto);
    }

    public Task<FolderIndexStatus?> IndexStatusAsync() =>
        _client.GetAsync("/api/v2/filesystem/index", CatalogJsonContext.Default.FolderIndexStatus);

    public Task<FolderIndexStatus?> RebuildIndexAsync() =>
        _client.PostAsync("/api/v2/filesystem/index", CatalogJsonContext.Default.FolderIndexStatus);

    public Task<List<FolderHit>?> SearchAsync(string query) =>
        _client.GetAsync($"/api/v2/filesystem/search?q={Uri.EscapeDataString(query)}", CatalogJsonContext.Default.ListFolderHit);
}
