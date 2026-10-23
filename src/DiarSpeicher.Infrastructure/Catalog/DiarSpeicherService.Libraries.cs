using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    internal sealed record LibrarySeriesCountRow(string LibraryId, long SeriesCount, long MissingSeries);
    internal sealed record LibraryMediaCountRow(string LibraryId, long MediaCount, long MissingMedia);

    public async Task<List<DiarSpeicherLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var (libWhere, libParams) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        var libSql = $"""
            SELECT l."Id", l."Name", l."Description", l."Path", l."Status", l."Emoji", l."ThumbnailPath", l."ThumbnailMeta",
                   l."CreatedAt", l."UpdatedAt", l."LastScannedAt", l."ConfigId"
            FROM "Libraries" l
            LEFT JOIN "LibraryConfigs" c ON c."Id" = l."ConfigId"
            WHERE {libWhere}
            ORDER BY l."Name" ASC;
            """;

        var libraries = (await conn.QueryAsync<Library>(libSql, libParams).WithConfigAsync(conn)).AsList();

        if (libraries.Count == 0) return [];

        var (seriesWhere, seriesParams) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        var seriesCountSql = $"""
            SELECT s."LibraryId",
                   COUNT(*) AS "SeriesCount",
                   SUM(CASE WHEN s."Status" = 'Missing' THEN 1 ELSE 0 END) AS "MissingSeries"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" IS NOT NULL AND {seriesWhere}
            GROUP BY s."LibraryId";
            """;

        var seriesCounts = (await conn.QueryAsync<LibrarySeriesCountRow>(
            seriesCountSql, seriesParams
        )).ToDictionary(x => x.LibraryId, StringComparer.Ordinal);

        var (mediaWhere, mediaParams) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        var mediaCountSql = $"""
            SELECT s."LibraryId",
                   COUNT(m."Id") AS "MediaCount",
                   SUM(CASE WHEN m."Status" = 'Missing' THEN 1 ELSE 0 END) AS "MissingMedia"
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" IS NOT NULL AND {mediaWhere}
            GROUP BY s."LibraryId";
            """;

        var mediaCounts = (await conn.QueryAsync<LibraryMediaCountRow>(
            mediaCountSql, mediaParams
        )).ToDictionary(x => x.LibraryId, StringComparer.Ordinal);

        return libraries.Select(l =>
        {
            var sc = seriesCounts.GetValueOrDefault(l.Id);
            var mc = mediaCounts.GetValueOrDefault(l.Id);

            var dto = ToLibraryDto(l, (int)(mc?.MediaCount ?? 0));
            dto.SeriesCount = (int)(sc?.SeriesCount ?? 0);
            dto.MissingSeries = (int)(sc?.MissingSeries ?? 0);
            dto.MissingVolumes = (int)(mc?.MissingMedia ?? 0);

            return dto;
        }).ToList();
    }

    public async Task<DiarSpeicherLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var (libWhere, libParams) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        libParams.Add("Id", id);

        var libSql = $"""
            SELECT l."Id", l."Name", l."Description", l."Path", l."Status", l."Emoji", l."ThumbnailPath", l."ThumbnailMeta",
                   l."CreatedAt", l."UpdatedAt", l."LastScannedAt", l."ConfigId"
            FROM "Libraries" l
            LEFT JOIN "LibraryConfigs" c ON c."Id" = l."ConfigId"
            WHERE l."Id" = @Id AND {libWhere}
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<Library>(libSql, libParams).WithConfigAsync(conn);

        var library = rows.FirstOrDefault();
        if (library == null) return null;

        const string mediaCountSql = """
            SELECT COUNT(m."Id")
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            WHERE s."LibraryId" = @LibraryId AND m."DeletedAt" IS NULL;
            """;

        var mediaCount = await conn.ExecuteScalarAsync<int>(
            mediaCountSql, new { LibraryId = id });

        const string seriesCountSql = """
            SELECT COUNT(*)
            FROM "Series"
            WHERE "LibraryId" = @LibraryId AND "DeletedAt" IS NULL;
            """;

        var seriesCount = await conn.ExecuteScalarAsync<int>(
            seriesCountSql, new { LibraryId = id });

        var dto = ToLibraryDto(library, mediaCount);
        dto.SeriesCount = seriesCount;
        return dto;
    }

    public async Task<bool> TriggerLibraryScanAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        parameters.Add("Id", libraryId);

        var checkSql = $@"SELECT 1 FROM ""Libraries"" l WHERE l.""Id"" = @Id AND {where} LIMIT 1;";
        var exists = await conn.ExecuteScalarAsync<int?>(checkSql, parameters);

        if (exists is null) return false;

        await _scannerQueue.QueueScanAsync(new ScanRequest(libraryId), ct);
        _logger.LogInformation("Enqueued scan task for library {LibraryId} by user {UserId}", libraryId, user.Id);
        return true;
    }

    public async Task<DiarSpeicherLibraryDto?> CreateLibraryAsync(AuthUser user, DiarSpeicherCreateLibraryInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Path))
            return null;

        if (!_libraryRoots.TryResolve(input.Path, out var fullPath))
        {
            throw new InvalidOperationException("Esa ruta esta fuera de las carpetas permitidas para bibliotecas.");
        }

        if (_libraryRoots.IsRoot(fullPath))
        {
            throw new InvalidOperationException("Esa ruta es una raiz de bibliotecas. Elige o crea una carpeta dentro de ella.");
        }

        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        var config = BuildConfig(input.Config);
        var library = new Library
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name.Trim(),
            Path = fullPath,
            Description = input.Description?.Trim(),
            Emoji = input.Emoji?.Trim(),
            Status = FileStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            Config = config
        };

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" (
                "ConvertRarToZip", "HardDeleteConversions", "DefaultReadingDir", "DefaultReadingMode",
                "GenerateFileHashes", "GenerateKoreaderHashes", "ProcessMetadata", "Watch",
                "LibraryPattern", "DefaultLibraryViewMode", "HideSeriesView", "LibraryType",
                "ThumbnailWidth", "ThumbnailHeight", "IgnoreRules"
            ) VALUES (
                @ConvertRarToZip, @HardDeleteConversions, @DefaultReadingDir, @DefaultReadingMode,
                @GenerateFileHashes, @GenerateKoreaderHashes, @ProcessMetadata, @Watch,
                @LibraryPattern, @DefaultLibraryViewMode, @HideSeriesView, @LibraryType,
                @ThumbnailWidth, @ThumbnailHeight, @IgnoreRules
            );
            SELECT last_insert_rowid();
            """;

        var configId = await conn.ExecuteScalarAsync<int>(insertConfigSql, config, tx);
        library.ConfigId = configId;
        config.Id = configId;

        const string insertLibSql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Description", "Emoji", "Status", "CreatedAt", "ConfigId")
            VALUES (@Id, @Name, @Path, @Description, @Emoji, @Status, @CreatedAt, @ConfigId);
            """;

        await conn.ExecuteAsync(insertLibSql, new
        {
            library.Id, library.Name, library.Path, library.Description, library.Emoji,
            Status = library.Status.ToString(),
            library.CreatedAt, library.ConfigId
        }, tx);
        await tx.CommitAsync(ct);

        await _scannerQueue.QueueScanAsync(new ScanRequest(library.Id), ct);
        return ToLibraryDto(library, 0);
    }

    public async Task<DiarSpeicherLibraryDto?> UpdateLibraryAsync(AuthUser user, string id, DiarSpeicherUpdateLibraryInput input, CancellationToken ct = default)
    {
        var existing = await GetLibraryByIdAsync(user, id, ct);
        if (existing == null) return null;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var (libWhere, libParams) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        libParams.Add("Id", id);

        var libSql = $"""
            SELECT l."Id", l."Name", l."Description", l."Path", l."Status", l."Emoji", l."ThumbnailPath", l."ThumbnailMeta",
                   l."CreatedAt", l."UpdatedAt", l."LastScannedAt", l."ConfigId"
            FROM "Libraries" l
            LEFT JOIN "LibraryConfigs" c ON c."Id" = l."ConfigId"
            WHERE l."Id" = @Id AND {libWhere}
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<Library>(libSql, libParams).WithConfigAsync(conn);

        var library = rows.FirstOrDefault();
        if (library == null) return null;

        if (!string.IsNullOrWhiteSpace(input.Name)) library.Name = input.Name.Trim();
        if (input.Description != null) library.Description = input.Description.Trim();
        if (input.Emoji != null) library.Emoji = input.Emoji.Trim();
        if (input.Config != null && library.Config != null) ApplyConfig(library.Config, input.Config);

        var oldPath = library.Path;
        var newPath = ResolveLibraryMove(library, input.Path);
        var now = DateTimeOffset.UtcNow;
        library.UpdatedAt = now;

        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            if (newPath != null) library.Path = newPath;

            const string updateLibSql = """
                UPDATE "Libraries"
                SET "Name" = @Name, "Description" = @Description, "Emoji" = @Emoji, "Path" = @Path, "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id;
                """;

            await conn.ExecuteAsync(updateLibSql, library, tx);

            if (library.Config != null)
            {
                const string updateConfigSql = """
                    UPDATE "LibraryConfigs"
                    SET "ConvertRarToZip" = @ConvertRarToZip,
                        "HardDeleteConversions" = @HardDeleteConversions,
                        "DefaultReadingDir" = @DefaultReadingDir,
                        "DefaultReadingMode" = @DefaultReadingMode,
                        "GenerateFileHashes" = @GenerateFileHashes,
                        "GenerateKoreaderHashes" = @GenerateKoreaderHashes,
                        "ProcessMetadata" = @ProcessMetadata,
                        "Watch" = @Watch,
                        "LibraryPattern" = @LibraryPattern,
                        "DefaultLibraryViewMode" = @DefaultLibraryViewMode,
                        "HideSeriesView" = @HideSeriesView,
                        "LibraryType" = @LibraryType,
                        "ThumbnailWidth" = @ThumbnailWidth,
                        "ThumbnailHeight" = @ThumbnailHeight,
                        "IgnoreRules" = @IgnoreRules
                    WHERE "Id" = @Id;
                    """;

                await conn.ExecuteAsync(updateConfigSql, library.Config, tx);
            }

            if (newPath != null)
            {
                await RepointLibraryPathsAsync(conn, tx, library.Id, oldPath, newPath, ct);
            }

            await tx.CommitAsync(ct);
        }

        if (newPath != null)
        {
            _logger.LogInformation("Biblioteca {Library} movida de {OldPath} a {NewPath}", library.Id, oldPath, newPath);
        }

        const string mediaCountSql = """
            SELECT COUNT(m."Id")
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            WHERE s."LibraryId" = @LibraryId AND m."DeletedAt" IS NULL;
            """;

        var mediaCount = await conn.ExecuteScalarAsync<int>(
            mediaCountSql, new { LibraryId = library.Id });

        return ToLibraryDto(library, mediaCount);
    }

    private string? ResolveLibraryMove(Library library, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;

        if (!_libraryRoots.TryResolve(requested, out var resolved))
        {
            throw new InvalidOperationException("Esa ruta esta fuera de las carpetas permitidas para bibliotecas.");
        }

        if (_libraryRoots.IsRoot(resolved))
        {
            throw new InvalidOperationException("Esa ruta es una raiz de bibliotecas. Elige o crea una carpeta dentro de ella.");
        }

        if (string.Equals(resolved, library.Path, StringComparison.Ordinal)) return null;

        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException("La carpeta destino no existe.");
        }

        return resolved;
    }

    private static async Task RepointLibraryPathsAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        string libraryId,
        string oldPath,
        string newPath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var cut = oldPath.Length + 1;

        const string updateMediaSql = """
            UPDATE "Media" SET "Path" = @NewPath || substr("Path", @Cut)
            WHERE substr("Path", 1, @OldPathLen) = @OldPath
              AND "SeriesId" IN (SELECT "Id" FROM "Series" WHERE "LibraryId" = @LibraryId);
            """;
        await conn.ExecuteAsync(updateMediaSql, new { NewPath = newPath, Cut = cut, OldPathLen = oldPath.Length, OldPath = oldPath, LibraryId = libraryId }, tx);

        const string updateSeriesSql = """
            UPDATE "Series" SET "Path" = @NewPath || substr("Path", @Cut)
            WHERE substr("Path", 1, @OldPathLen) = @OldPath AND "LibraryId" = @LibraryId;
            """;
        await conn.ExecuteAsync(updateSeriesSql, new { NewPath = newPath, Cut = cut, OldPathLen = oldPath.Length, OldPath = oldPath, LibraryId = libraryId }, tx);

        const string deleteScannedDirsSql = """
            DELETE FROM "ScannedDirectories" WHERE substr("Path", 1, @OldPathLen) = @OldPath;
            """;
        await conn.ExecuteAsync(deleteScannedDirsSql, new { OldPathLen = oldPath.Length, OldPath = oldPath }, tx);
    }

    public async Task<bool> DeleteLibraryAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var (libWhere, libParams) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        libParams.Add("Id", id);

        var selectSql = $@"SELECT ""Id"", ""Path"", ""ConfigId"" FROM ""Libraries"" l WHERE l.""Id"" = @Id AND {libWhere} LIMIT 1;";
        var library = await conn.QueryFirstOrDefaultAsync<Library>(selectSql, libParams);
        if (library == null) return false;

        if (deleteFiles)
        {
            EnsureTrashed(library.Path, "la carpeta");
            await PurgeIndexUnderPathAsync(user, library.Path, ct);
        }

        await PurgeLibraryContentAsync(conn, library.Id, ct);
        var libraryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(library.Path));

        await using var tx = await conn.BeginTransactionAsync(ct);
        const string deleteScannedDirsSql = @"DELETE FROM ""ScannedDirectories"" WHERE substr(""Path"", 1, @PathLen) = @Path;";
        await conn.ExecuteAsync(deleteScannedDirsSql, new { PathLen = libraryPath.Length, Path = libraryPath }, tx);

        const string deleteLibSql = @"DELETE FROM ""Libraries"" WHERE ""Id"" = @Id;";
        await conn.ExecuteAsync(deleteLibSql, new { Id = library.Id }, tx);

        const string deleteConfigSql = @"DELETE FROM ""LibraryConfigs"" WHERE ""Id"" = @ConfigId;";
        await conn.ExecuteAsync(deleteConfigSql, new { ConfigId = library.ConfigId }, tx);

        await tx.CommitAsync(ct);
        return true;
    }

    private async Task PurgeLibraryContentAsync(System.Data.Common.DbConnection conn, string libraryId, CancellationToken ct)
    {
        const string selectThumbsSql = """
            SELECT "ThumbnailPath" FROM "Series" WHERE "LibraryId" = @LibraryId AND "ThumbnailPath" IS NOT NULL
            UNION ALL
            SELECT m."ThumbnailPath" FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            WHERE s."LibraryId" = @LibraryId AND m."ThumbnailPath" IS NOT NULL;
            """;

        var thumbs = (await conn.QueryAsync<string>(
            selectThumbsSql, new { LibraryId = libraryId }
        )).AsList();

        foreach (var thumb in thumbs)
        {
            TryDeleteFile(thumb);
        }

        await using var tx = await conn.BeginTransactionAsync(ct);
        const string deleteMediaSql = """
            DELETE FROM "Media" WHERE "SeriesId" IN (SELECT "Id" FROM "Series" WHERE "LibraryId" = @LibraryId);
            """;
        await conn.ExecuteAsync(deleteMediaSql, new { LibraryId = libraryId }, tx);

        const string deleteSeriesSql = @"DELETE FROM ""Series"" WHERE ""LibraryId"" = @LibraryId;";
        var removedSeries = await conn.ExecuteAsync(deleteSeriesSql, new { LibraryId = libraryId }, tx);

        await tx.CommitAsync(ct);

        _logger.LogInformation("Purgadas {Series} series de la biblioteca {Library}", removedSeries, libraryId);
    }

    private static LibraryConfig BuildConfig(DiarSpeicherLibraryConfigDto? dto)
    {
        var config = new LibraryConfig { LibraryPattern = LibraryPattern.SeriesBased };
        if (dto != null)
        {
            ApplyConfig(config, dto);
        }

        return config;
    }

    private static void ApplyConfig(LibraryConfig config, DiarSpeicherLibraryConfigDto dto)
    {
        config.LibraryType = ParseEnum(dto.LibraryType, config.LibraryType);
        config.LibraryPattern = ParseEnum(dto.LibraryPattern, config.LibraryPattern);
        config.DefaultReadingDir = ParseEnum(dto.DefaultReadingDir, config.DefaultReadingDir);
        config.DefaultReadingMode = ParseEnum(dto.DefaultReadingMode, config.DefaultReadingMode);
        config.DefaultLibraryViewMode = ParseEnum(dto.DefaultLibraryViewMode, config.DefaultLibraryViewMode);
        config.ConvertRarToZip = dto.ConvertRarToZip;
        config.HardDeleteConversions = dto.HardDeleteConversions;
        config.GenerateFileHashes = dto.GenerateFileHashes;
        config.GenerateKoreaderHashes = dto.GenerateKoreaderHashes;
        config.ProcessMetadata = dto.ProcessMetadata;
        config.Watch = dto.Watch;
        config.HideSeriesView = dto.HideSeriesView;
        config.ThumbnailWidth = dto.ThumbnailWidth > 0 ? dto.ThumbnailWidth : config.ThumbnailWidth;
        config.ThumbnailHeight = dto.ThumbnailHeight > 0 ? dto.ThumbnailHeight : config.ThumbnailHeight;
        config.IgnoreRules = string.IsNullOrWhiteSpace(dto.IgnoreRules) ? null : dto.IgnoreRules.Trim();
    }

    private static TEnum ParseEnum<TEnum>(string? raw, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) ? parsed : fallback;
}
