namespace DiarSpeicher.Infrastructure.Catalog;

public interface IDiarSpeicherService
{
    Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> GetMediaAsync(AuthUser user, int page, int pageSize, bool newestFirst = false, CancellationToken ct = default);
    Task<DiarSpeicherMediaDto?> GetMediaByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<List<DiarSpeicherMediaDto>> GetKeepReadingAsync(AuthUser user, CancellationToken ct = default);
    Task<DiarSpeicherPageResponse<DiarSpeicherSeriesDto>> GetSeriesAsync(AuthUser user, string? libraryId, int page, int pageSize, CancellationToken ct = default);
    Task<DiarSpeicherSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> GetSeriesMediaAsync(AuthUser user, string seriesId, int page, int pageSize, CancellationToken ct = default);
    Task<DiarSpeicherSeriesDto?> UpdateSeriesAsync(AuthUser user, string id, DiarSpeicherUpdateSeriesInput input, CancellationToken ct = default);
    Task<(byte[] Data, string ContentType)?> GetSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default);
    Task<bool> SetSeriesThumbnailFromMediaAsync(AuthUser user, string seriesId, string mediaId, CancellationToken ct = default);
    Task<bool> SetSeriesThumbnailAsync(AuthUser user, string seriesId, Stream image, string fileName, CancellationToken ct = default);
    Task<bool> ClearSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default);
    Task<List<DiarSpeicherLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default);
    Task<DiarSpeicherLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<bool> TriggerLibraryScanAsync(AuthUser user, string libraryId, CancellationToken ct = default);
    Task<ExtractedPage?> GetMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default);
    Task<(string Path, string ContentType)?> GetMediaFileAsync(AuthUser user, string mediaId, CancellationToken ct = default);
    Task<bool> UpdateProgressAsync(AuthUser user, string mediaId, DiarSpeicherUpdateProgressInput input, CancellationToken ct = default);
    Task<DiarSpeicherEpubTocDto?> GetEpubTocAsync(AuthUser user, string mediaId, CancellationToken ct = default);
    Task<(byte[] Data, string ContentType)?> GetEpubResourceAsync(AuthUser user, string mediaId, string resourcePath, CancellationToken ct = default);
    Task<DiarSpeicherLibraryDto?> CreateLibraryAsync(AuthUser user, DiarSpeicherCreateLibraryInput input, CancellationToken ct = default);
    Task<DiarSpeicherLibraryDto?> UpdateLibraryAsync(AuthUser user, string id, DiarSpeicherUpdateLibraryInput input, CancellationToken ct = default);
    Task<bool> DeleteLibraryAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default);

    Task<bool> DeleteMediaAsync(AuthUser user, string id, bool deleteFile = false, CancellationToken ct = default);

    Task<bool> DeleteSeriesAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default);

    Task<DiarSpeicherMissingReportDto> GetMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default);

    Task<int> PurgeMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default);

    Task<int> PurgeIndexUnderPathAsync(AuthUser user, string path, CancellationToken ct = default);
    Task<UploadResult> UploadToLibraryAsync(AuthUser user, string libraryId, string? subpath, IEnumerable<DiarSpeicherUploadFileInput> files, CancellationToken ct = default);
    Task<UploadResult> UploadToLibraryAsync(AuthUser user, string libraryId, string? subpath, IAsyncEnumerable<DiarSpeicherUploadFileInput> files, CancellationToken ct = default);
    Task<DiarSpeicherSystemStatusDto> GetSystemStatusAsync(CancellationToken ct = default);
}
