using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Infrastructure.StumpV2;

public interface IStumpV2Service
{
    Task<StumpPageResponse<StumpMediaDto>> GetMediaAsync(AuthUser user, int page, int pageSize, CancellationToken ct = default);
    Task<StumpMediaDto?> GetMediaByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<List<StumpMediaDto>> GetKeepReadingAsync(AuthUser user, CancellationToken ct = default);
    Task<StumpPageResponse<StumpSeriesDto>> GetSeriesAsync(AuthUser user, string? libraryId, int page, int pageSize, CancellationToken ct = default);
    Task<StumpSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<StumpPageResponse<StumpMediaDto>> GetSeriesMediaAsync(AuthUser user, string seriesId, int page, int pageSize, CancellationToken ct = default);
    Task<StumpSeriesDto?> UpdateSeriesAsync(AuthUser user, string id, StumpUpdateSeriesInput input, CancellationToken ct = default);
    Task<(byte[] Data, string ContentType)?> GetSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default);
    Task<bool> SetSeriesThumbnailFromMediaAsync(AuthUser user, string seriesId, string mediaId, CancellationToken ct = default);
    Task<bool> SetSeriesThumbnailAsync(AuthUser user, string seriesId, Stream image, string fileName, CancellationToken ct = default);
    Task<bool> ClearSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default);
    Task<List<StumpLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default);
    Task<StumpLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<bool> TriggerLibraryScanAsync(AuthUser user, string libraryId, CancellationToken ct = default);
    Task<ExtractedPage?> GetMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default);
    Task<(string Path, string ContentType)?> GetMediaFileAsync(AuthUser user, string mediaId, CancellationToken ct = default);
    Task<bool> UpdateProgressAsync(AuthUser user, string mediaId, StumpUpdateProgressInput input, CancellationToken ct = default);
    Task<StumpEpubTocDto?> GetEpubTocAsync(AuthUser user, string mediaId, CancellationToken ct = default);
    Task<(byte[] Data, string ContentType)?> GetEpubResourceAsync(AuthUser user, string mediaId, string resourcePath, CancellationToken ct = default);
    Task<StumpLibraryDto?> CreateLibraryAsync(AuthUser user, StumpCreateLibraryInput input, CancellationToken ct = default);
    Task<StumpLibraryDto?> UpdateLibraryAsync(AuthUser user, string id, StumpUpdateLibraryInput input, CancellationToken ct = default);
    Task<bool> DeleteLibraryAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<UploadResult> UploadToLibraryAsync(AuthUser user, string libraryId, string? subpath, IEnumerable<StumpUploadFileInput> files, CancellationToken ct = default);
    Task<UploadResult> UploadToLibraryAsync(AuthUser user, string libraryId, string? subpath, IAsyncEnumerable<StumpUploadFileInput> files, CancellationToken ct = default);
    Task<StumpSystemStatusDto> GetSystemStatusAsync(CancellationToken ct = default);
}
