using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Reading;

public class EpubPageMapStore : IEpubPageMapStore
{
    private const int BuilderStripes = 64;

    private static readonly SemaphoreSlim[] Builders =
        [.. Enumerable.Range(0, BuilderStripes).Select(_ => new SemaphoreSlim(1, 1))];

    private static SemaphoreSlim BuilderFor(string key) =>
        Builders[(uint)StringComparer.Ordinal.GetHashCode(key) % BuilderStripes];

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<EpubPageMapStore> _logger;

    public EpubPageMapStore(ISqliteConnectionFactory connectionFactory, ILogger<EpubPageMapStore> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public static bool IsEpub(Media book) =>
        book.Extension.TrimStart('.').Equals("epub", StringComparison.OrdinalIgnoreCase)
        || book.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);

    public async Task<int> GetTotalPagesAsync(Media book, EpubDeviceProfile profile, CancellationToken ct = default)
    {
        var profileKey = EpubRasterizer.BuildProfileKey(profile);
        var fileModifiedAt = GetFileModifiedAt(book.Path);

        var stored = await FindAsync(book.Id, profileKey, ct);
        if (IsUsable(stored, fileModifiedAt)) return stored!.TotalPages;

        var gate = BuilderFor($"{book.Id}:{profileKey}");
        await gate.WaitAsync(ct);
        try
        {
            stored = await FindAsync(book.Id, profileKey, ct);
            if (IsUsable(stored, fileModifiedAt)) return stored!.TotalPages;

            var measured = await MeasureAsync(book, profile, ct);
            if (measured <= 0)
            {
                return stored?.TotalPages > 0 ? stored.TotalPages : Math.Max(1, book.Pages);
            }

            return await SaveAsync(book, profileKey, measured, fileModifiedAt, profile.Name, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<EpubPageMap?> FindAsync(string mediaId, string profileKey, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string sql = """
            SELECT "MediaId", "ProfileKey", "TotalPages", "FileModifiedAt", "BuiltAt"
            FROM "EpubPageMaps"
            WHERE "MediaId" = @MediaId AND "ProfileKey" = @ProfileKey
            LIMIT 1;
            """;
        return await conn.QueryFirstOrDefaultAsync<EpubPageMap>(sql, new { MediaId = mediaId, ProfileKey = profileKey });
    }

    private static bool IsUsable(EpubPageMap? stored, DateTimeOffset fileModifiedAt) =>
        stored != null && stored.TotalPages > 0 && SameFile(stored.FileModifiedAt, fileModifiedAt);

    private async Task<int> SaveAsync(
        Media book,
        string profileKey,
        int measured,
        DateTimeOffset fileModifiedAt,
        string profileName,
        CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string upsertSql = """
            INSERT INTO "EpubPageMaps" ("MediaId", "ProfileKey", "TotalPages", "FileModifiedAt", "BuiltAt")
            VALUES (@MediaId, @ProfileKey, @TotalPages, @FileModifiedAt, @BuiltAt)
            ON CONFLICT("MediaId", "ProfileKey") DO UPDATE SET
                "TotalPages" = excluded."TotalPages",
                "FileModifiedAt" = excluded."FileModifiedAt",
                "BuiltAt" = excluded."BuiltAt";
            """;

        var now = DateTimeOffset.UtcNow;
        await conn.ExecuteAsync(upsertSql, new { MediaId = book.Id, ProfileKey = profileKey, TotalPages = measured, FileModifiedAt = fileModifiedAt, BuiltAt = now });

        _logger.LogInformation(
            "[EPUB] {Total} paginas para {Name} con el perfil {Profile}",
            measured, book.Name, profileName);
        return measured;
    }

    private async Task<int> MeasureAsync(Media book, EpubDeviceProfile profile, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(book.Path)) return 0;

            await using var archive = await ZipFile.OpenReadAsync(book.Path, ct);
            var spine = await EpubBookProcessor.GetSpineEntriesAsync(archive, ct);
            if (spine.Count == 0) spine = EpubBookProcessor.GetFallbackHtmlEntries(archive);
            var cover = await EpubBookProcessor.FindCoverEntryAsync(archive, ct);
            var map = await EpubRasterizer.GetOrBuildPageMapAsync(archive, book.Path, spine, cover, profile, ct);
            return map.TotalPages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo medir el mapa de paginas de {Path}", book.Path);
            return 0;
        }
    }

    private static DateTimeOffset GetFileModifiedAt(string path)
    {
        try
        {
            return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : default;
        }
        catch
        {
            // Sin fecha no se puede saber si el total guardado sigue valiendo: default nunca
            // casa con la comprobacion, asi que el libro se vuelve a medir.
            return default;
        }
    }

    private static bool SameFile(DateTimeOffset stored, DateTimeOffset current) =>
        Math.Abs((stored - current).TotalSeconds) < 1;
}
