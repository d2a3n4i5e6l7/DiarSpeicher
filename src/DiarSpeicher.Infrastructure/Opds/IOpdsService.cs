using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Infrastructure.Opds;

public interface IOpdsService
{
    Task<string> GetCatalogXmlAsync(AuthUser user, string? apiKey, CancellationToken ct = default);
    string GetOpenSearchXml(string? apiKey);
    Task<string> GetSearchFeedXmlAsync(AuthUser user, string? query, string? apiKey, CancellationToken ct = default);
    Task<string> GetKeepReadingFeedXmlAsync(AuthUser user, string? apiKey, CancellationToken ct = default);
    Task<string> GetLibrariesFeedAsync(AuthUser user, string? search, string? apiKey, CancellationToken ct = default);
    Task<string> GetLibrarySeriesFeedAsync(AuthUser user, string libraryId, int page, string? apiKey, CancellationToken ct = default);
    Task<string> GetSeriesFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default);
    Task<string> GetLatestSeriesFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default);
    Task<string> GetSeriesBooksFeedAsync(AuthUser user, string seriesId, int page, string? apiKey, CancellationToken ct = default);
    Task<string> GetBooksFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default);
    Task<string> GetLatestBooksFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default);
    Task<(ExtractedPage? Page, Media? Media)> GetBookPageAsync(AuthUser user, string bookId, int pageNumber, bool zeroBased, bool trackProgression = true, CancellationToken ct = default);
    Task<Media?> GetMediaForDownloadAsync(AuthUser user, string bookId, CancellationToken ct = default);
    Task<(byte[]? Data, string ContentType)> GetBookThumbnailAsync(AuthUser user, string bookId, CancellationToken ct = default);
}
