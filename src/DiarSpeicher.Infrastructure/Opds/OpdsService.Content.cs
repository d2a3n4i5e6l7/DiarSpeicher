using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public partial class OpdsService
{
    public async Task<(OpenedPage? Page, Media? Media)> OpenBookPageAsync(
        AuthUser user,
        string bookId,
        int pageNumber,
        PageRequestKind kind = PageRequestKind.Reading,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = bookId;

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        var book = await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);

        if (book == null) return (null, null);

        var correctPage = pageNumber;
        var isEpub = EpubPageMapStore.IsEpub(book);
        if (correctPage < 1 || (!isEpub && book.Pages > 0 && correctPage > book.Pages))
        {
            return (null, book);
        }

        if (kind == PageRequestKind.Reading && !string.IsNullOrWhiteSpace(user.Id))
        {
            await RecordReadingProgressAsync(user, book, correctPage, ct);
        }

        var page = await _bookProcessor.OpenPageAsync(book.Path, correctPage, ct);
        return (page, book);
    }

    private async Task RecordReadingProgressAsync(AuthUser user, Media book, int correctPage, CancellationToken ct)
    {
        try
        {
            await _progress.RecordAsync(user, book, new ReadingProgressUpdate(correctPage), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record reading progression for user {UserId}, book {BookId}", user.Id, book.Id);
        }
    }

    public async Task<Media?> GetMediaForDownloadAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = bookId;

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        return await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);
    }

    public async Task<(byte[]? Data, string ContentType)> GetBookThumbnailAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        var book = await GetMediaForDownloadAsync(user, bookId, ct);
        if (book == null) return (null, "image/jpeg");

        if (!string.IsNullOrWhiteSpace(book.ThumbnailPath) && File.Exists(book.ThumbnailPath))
        {
            var bytes = await File.ReadAllBytesAsync(book.ThumbnailPath, ct);
            var ext = Path.GetExtension(book.ThumbnailPath);
            var mime = ContentTypeExtensions.FromExtension(ext).MimeType();
            return (bytes, mime);
        }

        return (null, "image/jpeg");
    }
}
