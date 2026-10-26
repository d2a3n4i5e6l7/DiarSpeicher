using System.Globalization;
using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data.Extensions;

namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public async Task<DiarSpeicherPageResponse<DiarSpeicherSeriesDto>> GetSeriesAsync(AuthUser user, string? libraryId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            parameters.LibraryId = libraryId;
        }

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.InLibrary};
            """;

        var total = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        parameters.PageSize = pageSize;
        parameters.Offset = page * pageSize;

        const string selectSql = $"""
            SELECT s."Id", s."Name", s."Description", s."Path", s."Status", s."ThumbnailPath", s."ThumbnailMeta",
                   s."CreatedAt", s."UpdatedAt", s."DeletedAt", s."LibraryId",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "MediaCount"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.InLibrary}
            ORDER BY s."Name" ASC
            LIMIT @PageSize OFFSET @Offset;
            """;

        var rows = await conn.QueryAsync<CountedSeries>(selectSql, parameters).WithMetadataAsync(conn);
        var dtos = rows.Select(s => CatalogDtoMapper.ToSeriesDto(s, (int)s.MediaCount)).ToList();

        return new DiarSpeicherPageResponse<DiarSpeicherSeriesDto>
        {
            Data = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<DiarSpeicherSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = id;

        const string selectSql = $"""
            SELECT s."Id", s."Name", s."Description", s."Path", s."Status", s."ThumbnailPath", s."ThumbnailMeta",
                   s."CreatedAt", s."UpdatedAt", s."DeletedAt", s."LibraryId",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "MediaCount"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @Id AND {CatalogSqlFilters.SeriesVisible}
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<CountedSeries>(selectSql, parameters).WithMetadataAsync(conn);
        return rows.Select(s => CatalogDtoMapper.ToSeriesDto(s, (int)s.MediaCount)).FirstOrDefault();
    }

    public async Task<DiarSpeicherSeriesDto?> UpdateSeriesAsync(AuthUser user, string id, DiarSpeicherUpdateSeriesInput input, CancellationToken ct = default)
    {
        var existing = await GetSeriesByIdAsync(user, id, ct);
        if (existing == null) return null;

        var name = !string.IsNullOrWhiteSpace(input.Name) ? input.Name.Trim() : existing.Name;
        var description = ResolveDescription(input.Description, existing.Description);
        var now = DateTimeOffset.UtcNow;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string updateSql = """
            UPDATE "Series"
            SET "Name" = @Name, "Description" = @Description, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id;
            """;

        await conn.ExecuteAsync(updateSql, new { Name = name, Description = description, UpdatedAt = now, Id = id });

        return await GetSeriesByIdAsync(user, id, ct);
    }

    public Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> GetSeriesMediaAsync(AuthUser user, string seriesId, int page, int pageSize, CancellationToken ct = default)
    {
        return PageMediaAsync(user, seriesId, newestFirst: false, page: page, pageSize: pageSize, ct: ct);
    }

    private async Task<IdPathThumbnailRow?> FetchSeriesBasicAsync(AuthUser user, string seriesId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = seriesId;

        const string selectSql = $"""
            SELECT s."Id", s."Path", s."ThumbnailPath"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @Id AND {CatalogSqlFilters.SeriesVisible}
            LIMIT 1;
            """;

        return await conn.QueryFirstOrDefaultAsync<IdPathThumbnailRow>(
            selectSql, parameters);
    }

    public async Task<bool> DeleteSeriesAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default)
    {
        var series = await FetchSeriesBasicAsync(user, id, ct);
        if (series == null) return false;

        var seriesPath = series.Path;

        if (deleteFiles)
        {
            EnsureTrashed(seriesPath, "la carpeta de la serie");
            await PurgeIndexUnderPathAsync(user, seriesPath, ct);
        }

        var remaining = await FetchSeriesBasicAsync(user, id, ct);
        if (remaining == null) return true;

        if (!string.IsNullOrWhiteSpace(remaining.ThumbnailPath))
        {
            TryDeleteFile(remaining.ThumbnailPath);
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        const string selectMediaThumbs = @"SELECT ""ThumbnailPath"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId AND ""ThumbnailPath"" IS NOT NULL;";
        var thumbs = (await conn.QueryAsync<string>(
            selectMediaThumbs, new { SeriesId = id }
        )).AsRowList();

        foreach (var thumb in thumbs)
        {
            TryDeleteFile(thumb);
        }

        await using var tx = await conn.BeginTransactionAsync(ct);
        const string deleteMediaSql = @"DELETE FROM ""Media"" WHERE ""SeriesId"" = @SeriesId;";
        await conn.ExecuteAsync(deleteMediaSql, new { SeriesId = id }, tx);

        const string deleteSeriesSql = @"DELETE FROM ""Series"" WHERE ""Id"" = @Id;";
        await conn.ExecuteAsync(deleteSeriesSql, new { Id = id }, tx);
        await tx.CommitAsync(ct);

        return true;
    }

    public async Task<(byte[] Data, string ContentType)?> GetSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        var series = await FetchSeriesBasicAsync(user, seriesId, ct);
        if (series == null) return null;

        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath) && File.Exists(series.ThumbnailPath))
        {
            return await ReadImageAsync(series.ThumbnailPath, ct);
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var mediaParams = CatalogSqlFilters.ArgsFor(user);
        mediaParams.SeriesId = seriesId;

        const string fallbackSql = $"""
            SELECT m."ThumbnailPath"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."SeriesId" = @SeriesId AND m."ThumbnailPath" IS NOT NULL AND {CatalogSqlFilters.MediaVisible}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT 1;
            """;

        var fallback = await conn.QueryFirstOrDefaultAsync<string>(
            fallbackSql, mediaParams);

        return string.IsNullOrWhiteSpace(fallback) || !File.Exists(fallback)
            ? null
            : await ReadImageAsync(fallback, ct);
    }

    public async Task<bool> SetSeriesThumbnailFromMediaAsync(AuthUser user, string seriesId, string mediaId, CancellationToken ct = default)
    {
        var series = await FetchSeriesBasicAsync(user, seriesId, ct);
        if (series == null) return false;

        var media = await FetchMediaForUserAsync(user, mediaId, ct);
        if (media == null || string.IsNullOrWhiteSpace(media.ThumbnailPath) || !File.Exists(media.ThumbnailPath))
        {
            return false;
        }

        var source = media.ThumbnailPath;
        var target = BuildSeriesThumbnailPath(seriesId, Path.GetExtension(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);

        await ApplySeriesThumbnailAsync(seriesId, series.ThumbnailPath, target, ct);
        return true;
    }

    public async Task<bool> SetSeriesThumbnailAsync(AuthUser user, string seriesId, Stream image, string fileName, CancellationToken ct = default)
    {
        var series = await FetchSeriesBasicAsync(user, seriesId, ct);
        if (series == null) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!CoverExtensions.Contains(extension)) return false;

        var target = BuildSeriesThumbnailPath(seriesId, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await image.CopyToAsync(output, ct);
        }

        await ApplySeriesThumbnailAsync(seriesId, series.ThumbnailPath, target, ct);
        return true;
    }

    public async Task<bool> ClearSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        var series = await FetchSeriesBasicAsync(user, seriesId, ct);
        if (series == null) return false;

        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath) && File.Exists(series.ThumbnailPath))
        {
            TryDeleteFile(series.ThumbnailPath);
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string clearSql = """
            UPDATE "Series"
            SET "ThumbnailPath" = NULL, "ThumbnailMeta" = NULL, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id;
            """;

        await conn.ExecuteAsync(clearSql, new { UpdatedAt = DateTimeOffset.UtcNow, Id = seriesId });

        return true;
    }

    private async Task ApplySeriesThumbnailAsync(string seriesId, string? oldPath, string newPath, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(oldPath)
            && !string.Equals(oldPath, newPath, StringComparison.Ordinal)
            && File.Exists(oldPath))
        {
            TryDeleteFile(oldPath);
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string updateSql = """
            UPDATE "Series"
            SET "ThumbnailPath" = @ThumbnailPath,
                "ThumbnailMeta" = @ThumbnailMeta,
                "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id;
            """;

        var now = DateTimeOffset.UtcNow;
        var meta = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        await conn.ExecuteAsync(updateSql, new { ThumbnailPath = newPath, ThumbnailMeta = meta, UpdatedAt = now, Id = seriesId });
    }

    private string BuildSeriesThumbnailPath(string seriesId, string extension) =>
        Path.Combine(_storage.ResolveThumbnailsPath(), "series", $"{seriesId}{extension}");

    private static string? ResolveDescription(string? requested, string? current)
    {
        if (requested == null) return current;
        return string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
    }

    private static async Task<(byte[] Data, string ContentType)> ReadImageAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var mime = ContentTypeExtensions.FromExtension(Path.GetExtension(path)).MimeType();
        return (bytes, mime);
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "No se pudo borrar la miniatura {Path}", path);
        }
    }
}
