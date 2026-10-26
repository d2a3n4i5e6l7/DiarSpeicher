using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Komga;

public partial class KomgaService
{
    public async Task<List<KomgaBookPageDto>> GetBookPagesAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = id;

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
        if (book == null || book.Pages <= 0) return [];

        if (EpubPageMapStore.IsEpub(book))
        {
            return await GetEpubBookPagesAsync(book, ct);
        }

        var pageSql = """
            SELECT "FileName", "Height", "MediaId", "MediaType", "Number", "SizeBytes", "Width"
            FROM "MediaPages"
            WHERE "MediaId" = @MediaId
            ORDER BY "Number" ASC;
            """;

        var measured = (await conn.QueryAsync<MediaPage>(
            pageSql, new { MediaId = book.Id })).ToList();

        if (measured.Count == 0)
        {
            measured = await MeasurePagesAsync(book, ct);
        }

        if (measured.Count > 0)
        {
            return measured.Select(p => new KomgaBookPageDto
            {
                Number = p.Number,
                FileName = p.FileName,
                MediaType = p.MediaType,
                Width = p.Width,
                Height = p.Height,
                SizeBytes = p.SizeBytes
            }).ToList();
        }

        var fallbackPages = new List<KomgaBookPageDto>();
        for (var i = 1; i <= book.Pages; i++)
        {
            fallbackPages.Add(new KomgaBookPageDto
            {
                Number = i,
                FileName = $"page_{i:D4}.jpg",
                MediaType = "image/jpeg"
            });
        }
        return fallbackPages;
    }

    private async Task<List<MediaPage>> MeasurePagesAsync(Media book, CancellationToken ct)
    {
        if (_pageProcessor is null) return [];
        if (EpubPageMapStore.IsEpub(book)) return [];

        _logger.LogInformation(
            "Midiendo {Pages} paginas de {BookId}; la primera apertura de un libro es lenta",
            book.Pages, book.Id);

        var analyzed = await _pageProcessor.AnalyzeAsync(book.Path, new BookAnalysisOptions { MeasurePages = true }, ct);

        var rows = analyzed.PageDimensions.Select(p => new MediaPage
        {
            MediaId = book.Id,
            Number = p.Number,
            FileName = p.FileName,
            MediaType = p.MediaType,
            Width = p.Width,
            Height = p.Height,
            SizeBytes = p.SizeBytes
        }).ToList();

        if (rows.Count > 0)
        {
            await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
            const string insertSql = """
                INSERT INTO "MediaPages" ("MediaId", "Number", "FileName", "MediaType", "Width", "Height", "SizeBytes")
                VALUES (@MediaId, @Number, @FileName, @MediaType, @Width, @Height, @SizeBytes)
                ON CONFLICT ("MediaId", "Number") DO NOTHING;
                """;

            await conn.ExecuteAsync(insertSql, rows);

            const string selectSql = """
                SELECT "FileName", "Height", "MediaId", "MediaType", "Number", "SizeBytes", "Width"
                FROM "MediaPages"
                WHERE "MediaId" = @MediaId
                ORDER BY "Number" ASC;
                """;

            return (await conn.QueryAsync<MediaPage>(
                selectSql, new { MediaId = book.Id })).ToList();
        }

        return rows;
    }

    public async Task<bool> UpdateReadProgressAsync(
        AuthUser user,
        string bookId,
        int? page,
        bool? completed,
        CancellationToken ct = default)
    {
        if (completed != true && page is null) return false;

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
        if (book == null) return false;

        var update = completed == true
            ? new ReadingProgressUpdate(int.MaxValue, true)
            : new ReadingProgressUpdate(page!.Value);
        var ok = await _progress.RecordAsync(user, book, update, ct);
        if (ok)
        {
            _logger.LogTrace("Updated Komga read progress for user {UserId}, book {BookId}, page {Page}", user.Id, bookId, page);
        }
        return ok;
    }

    public async Task<bool> DeleteReadProgressAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return false;

        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);

        // An EPUB session also holds the percentage the e-reader protocols write, so only the
        // image side is cleared; the row goes only when nothing else lives in it.
        const string clearImageSql = """
            UPDATE "ReadingSessions"
            SET "RenderedPage" = NULL, "RenderedTotalPages" = NULL, "RenderedProfileKey" = NULL, "RenderedStatus" = NULL
            WHERE "MediaId" = @BookId AND "UserId" = @UserId AND "RenderedPage" IS NOT NULL;
            """;
        const string deleteSql = """
            DELETE FROM "ReadingSessions"
            WHERE "MediaId" = @BookId AND "UserId" = @UserId
              AND "MediaId" NOT IN (
                  SELECT "Id" FROM "Media" WHERE lower(trim("Extension", '.')) = 'epub' OR lower("Path") LIKE '%.epub');
            """;

        var args = new { BookId = bookId, UserId = user.Id };
        var rows = await conn.ExecuteAsync(clearImageSql, args) + await conn.ExecuteAsync(deleteSql, args);
        if (rows > 0)
        {
            _logger.LogTrace("Deleted Komga read progress for user {UserId}, book {BookId}", user.Id, bookId);
            return true;
        }
        return false;
    }

    private async Task CleanStalePagesAsync(string mediaId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """DELETE FROM "MediaPages" WHERE "MediaId" = @MediaId;""";
        await conn.ExecuteAsync(sql, new { MediaId = mediaId });
    }

    private async Task<List<KomgaBookPageDto>> GetEpubBookPagesAsync(Media book, CancellationToken ct)
    {
        await CleanStalePagesAsync(book.Id, ct);

        var profile = _epubProfileProvider != null
            ? await _epubProfileProvider.GetCurrentProfileAsync(ct)
            : EpubDeviceProfile.GetDefaults()[0];

        await using var archive = await ZipFile.OpenReadAsync(book.Path, ct);
        var spine = await EpubBookProcessor.GetSpineEntriesAsync(archive, ct);
        if (spine.Count == 0) spine = EpubBookProcessor.GetFallbackHtmlEntries(archive);
        var cover = await EpubBookProcessor.FindCoverEntryAsync(archive, ct);
        var map = await EpubRasterizer.GetOrBuildPageMapAsync(archive, book.Path, spine, cover, profile, ct);

        var pages = new List<KomgaBookPageDto>(map.TotalPages);
        for (var i = 1; i <= map.TotalPages; i++)
        {
            pages.Add(BuildBookPageDto(map.Pages[i - 1], i, profile));
        }
        return pages;
    }

    private static KomgaBookPageDto BuildBookPageDto(EpubSubpageTarget target, int pageNumber, EpubDeviceProfile profile)
    {
        int width = profile.Width;
        int? height = profile.Height;
        string mediaType = "image/webp";

        if (target.IsImageOnly && target.ImageWidth.HasValue && target.ImageHeight.HasValue)
        {
            width = target.ImageWidth.Value;
            height = target.ImageHeight.Value;
            var ext = Path.GetExtension(target.EntryFullName).ToLowerInvariant();
            mediaType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        return new KomgaBookPageDto
        {
            Number = pageNumber,
            FileName = $"page_{pageNumber:D4}.webp",
            MediaType = mediaType,
            Width = width,
            Height = height
        };
    }
}
