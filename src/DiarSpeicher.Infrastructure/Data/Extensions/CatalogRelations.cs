using System.Data;
using Dapper;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

/// <summary>
/// Dapper.AOT does not generate multi-mapping (splitOn): each relation is loaded with a second
/// query by key.
/// </summary>
public static class CatalogRelations
{
    public static Task<IEnumerable<TSeries>> WithMetadataAsync<TSeries>(this Task<IEnumerable<TSeries>> query, IDbConnection conn, IDbTransaction? tx = null)
        where TSeries : Series =>
        LoadAsync(query, rows => AttachMetadataAsync(conn, rows, tx));

    public static Task<IEnumerable<Media>> WithMetadataAsync(this Task<IEnumerable<Media>> query, IDbConnection conn, IDbTransaction? tx = null) =>
        LoadAsync(query, rows => AttachMetadataAsync(conn, rows, tx));

    public static Task<IEnumerable<Media>> WithSeriesAsync(this Task<IEnumerable<Media>> query, IDbConnection conn, IDbTransaction? tx = null) =>
        LoadAsync(query, rows => AttachSeriesAsync(conn, rows, tx));

    public static Task<IEnumerable<Library>> WithConfigAsync(this Task<IEnumerable<Library>> query, IDbConnection conn, IDbTransaction? tx = null) =>
        LoadAsync(query, rows => AttachConfigAsync(conn, rows, tx));

    public static Task<IEnumerable<User>> WithAgeRestrictionAsync(this Task<IEnumerable<User>> query, IDbConnection conn, IDbTransaction? tx = null) =>
        LoadAsync(query, rows => AttachAgeRestrictionAsync(conn, rows, tx));

    private static async Task AttachMetadataAsync(IDbConnection conn, IReadOnlyCollection<Series> series, IDbTransaction? tx)
    {
        if (series.Count == 0) return;

        const string sql = """
            SELECT "SeriesId", "AgeRating", "Characters", "Booktype", "Comicid", "ComicImage", "DescriptionFormatted",
                   "Genres", "Imprint", "Links", "MetaType", "PublicationRun", "Publisher", "Status", "Summary",
                   "Title", "TotalIssues", "Volume", "Writers", "Year"
            FROM "SeriesMetadata"
            WHERE "SeriesId" IN (SELECT value FROM json_each(@Keys));
            """;

        var rows = await conn.QueryAsync<SeriesMetadata>(sql, new { Keys = SqlKeys.ToJson(series.Select(s => s.Id)) }, tx);
        Attach(series, s => s.Id, rows, m => m.SeriesId, (s, m) => s.Metadata = m);
    }

    private static async Task AttachMetadataAsync(IDbConnection conn, IReadOnlyCollection<Media> media, IDbTransaction? tx)
    {
        if (media.Count == 0) return;

        const string sql = """
            SELECT "Id", "MediaId", "AgeRating", "Characters", "Colorists", "CoverArtists", "Format", "Day", "Editors",
                   "Genres", "IdentifierAmazon", "IdentifierCalibre", "IdentifierGoogle", "IdentifierIsbn",
                   "IdentifierMobiAsin", "IdentifierUuid", "Inkers", "Language", "Letterers", "Links", "Month", "Notes",
                   "Number", "PageCount", "Pencillers", "Publisher", "Series", "SeriesGroup", "StoryArc", "StoryArcNumber",
                   "Summary", "Teams", "Title", "TitleSort", "Volume", "Writers", "Year"
            FROM "MediaMetadata"
            WHERE "MediaId" IN (SELECT value FROM json_each(@Keys));
            """;

        var rows = await conn.QueryAsync<MediaMetadata>(sql, new { Keys = SqlKeys.ToJson(media.Select(m => m.Id)) }, tx);
        Attach(media, m => m.Id, rows, mm => mm.MediaId, (m, mm) => m.Metadata = mm);
    }

    private static async Task AttachSeriesAsync(IDbConnection conn, IReadOnlyCollection<Media> media, IDbTransaction? tx)
    {
        if (media.Count == 0) return;

        const string sql = """
            SELECT "Id", "Name", "Description", "Path", "Status", "ThumbnailPath", "ThumbnailMeta",
                   "CreatedAt", "UpdatedAt", "DeletedAt", "LibraryId"
            FROM "Series"
            WHERE "Id" IN (SELECT value FROM json_each(@Keys));
            """;

        var rows = await conn.QueryAsync<Series>(sql, new { Keys = SqlKeys.ToJson(media.Select(m => m.SeriesId)) }, tx);
        Attach(media, m => m.SeriesId, rows, s => s.Id, (m, s) => m.Series = s);
    }

    private static async Task AttachConfigAsync(IDbConnection conn, IReadOnlyCollection<Library> libraries, IDbTransaction? tx)
    {
        if (libraries.Count == 0) return;

        const string sql = """
            SELECT "Id", "ConvertRarToZip", "HardDeleteConversions", "DefaultReadingDir", "DefaultReadingMode",
                   "GenerateFileHashes", "GenerateKoreaderHashes", "ProcessMetadata", "Watch", "LibraryPattern",
                   "DefaultLibraryViewMode", "HideSeriesView", "LibraryType", "ThumbnailWidth", "ThumbnailHeight", "IgnoreRules"
            FROM "LibraryConfigs"
            WHERE "Id" IN (SELECT value FROM json_each(@Keys));
            """;

        var rows = await conn.QueryAsync<LibraryConfig>(sql, new { Keys = SqlKeys.ToJson(libraries.Select(l => l.ConfigId)) }, tx);
        Attach(libraries, l => l.ConfigId, rows, c => c.Id, (l, c) => l.Config = c);
    }

    private static async Task AttachAgeRestrictionAsync(IDbConnection conn, IReadOnlyCollection<User> users, IDbTransaction? tx)
    {
        if (users.Count == 0) return;

        const string sql = """
            SELECT "Id", "UserId", "Age", "RestrictOnUnset"
            FROM "AgeRestrictions"
            WHERE "UserId" IN (SELECT value FROM json_each(@Keys));
            """;

        var rows = await conn.QueryAsync<AgeRestriction>(sql, new { Keys = SqlKeys.ToJson(users.Select(u => u.Id)) }, tx);
        Attach(users, u => u.Id, rows, a => a.UserId, (u, a) => u.AgeRestriction = a);
    }

    private static async Task<IEnumerable<T>> LoadAsync<T>(Task<IEnumerable<T>> query, Func<List<T>, Task> attach)
    {
        var rows = (await query).AsRowList();
        await attach(rows);
        return rows;
    }

    private static void Attach<TOwner, TRelated, TKey>(
        IEnumerable<TOwner> owners,
        Func<TOwner, TKey?> ownerKey,
        IEnumerable<TRelated> related,
        Func<TRelated, TKey?> relatedKey,
        Action<TOwner, TRelated> assign)
        where TKey : notnull
    {
        var byKey = new Dictionary<TKey, TRelated>();
        foreach (var item in related)
        {
            if (relatedKey(item) is { } key) byKey.TryAdd(key, item);
        }

        foreach (var owner in owners)
        {
            if (ownerKey(owner) is { } key && byKey.TryGetValue(key, out var match)) assign(owner, match);
        }
    }
}
