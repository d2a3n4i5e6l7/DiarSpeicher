using Dapper;
using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Infrastructure.Data;

public static class SqliteDatabaseInitializer
{
    private const string SchemaSql = """
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA temp_store = MEMORY;

        CREATE TABLE IF NOT EXISTS "LibraryConfigs" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "ConvertRarToZip" INTEGER NOT NULL DEFAULT 0,
            "HardDeleteConversions" INTEGER NOT NULL DEFAULT 0,
            "DefaultReadingDir" TEXT NOT NULL DEFAULT 'LeftToRight',
            "DefaultReadingMode" TEXT NOT NULL DEFAULT 'Paged',
            "GenerateFileHashes" INTEGER NOT NULL DEFAULT 1,
            "GenerateKoreaderHashes" INTEGER NOT NULL DEFAULT 1,
            "ProcessMetadata" INTEGER NOT NULL DEFAULT 1,
            "Watch" INTEGER NOT NULL DEFAULT 0,
            "LibraryPattern" TEXT NOT NULL DEFAULT 'SeriesBased',
            "DefaultLibraryViewMode" TEXT NOT NULL DEFAULT 'Grid',
            "HideSeriesView" INTEGER NOT NULL DEFAULT 0,
            "LibraryType" TEXT NOT NULL DEFAULT 'Manga',
            "ThumbnailWidth" INTEGER NOT NULL DEFAULT 300,
            "ThumbnailHeight" INTEGER NOT NULL DEFAULT 450,
            "IgnoreRules" TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS "ScannedDirectories" (
            "Path" TEXT PRIMARY KEY,
            "LastMTime" INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "Tags" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "Users" (
            "Id" TEXT PRIMARY KEY,
            "Username" TEXT NOT NULL,
            "IsServerOwner" INTEGER NOT NULL DEFAULT 0,
            "AvatarPath" TEXT NULL,
            "AvatarMeta" TEXT NULL,
            "AvatarUpdatedAt" TEXT NULL,
            "CreatedAt" TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "Libraries" (
            "Id" TEXT PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Description" TEXT NULL,
            "Path" TEXT NOT NULL,
            "Status" TEXT NOT NULL DEFAULT 'Ready',
            "Emoji" TEXT NULL,
            "ThumbnailPath" TEXT NULL,
            "ThumbnailMeta" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            "LastScannedAt" TEXT NULL,
            "ConfigId" INTEGER NOT NULL,
            FOREIGN KEY ("ConfigId") REFERENCES "LibraryConfigs" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "AgeRestrictions" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "UserId" TEXT NOT NULL,
            "Age" INTEGER NOT NULL,
            "RestrictOnUnset" INTEGER NOT NULL DEFAULT 1,
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "UserPreferences" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "UserId" TEXT NOT NULL,
            "Locale" TEXT NOT NULL DEFAULT 'en',
            "AppTheme" TEXT NOT NULL DEFAULT 'Dark',
            "AppFont" TEXT NOT NULL DEFAULT 'Default',
            "EnableCompactDisplay" INTEGER NOT NULL DEFAULT 0,
            "EpubProfilesJson" TEXT NULL,
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "LibraryExclusions" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "LibraryId" TEXT NOT NULL,
            "UserId" TEXT NOT NULL,
            FOREIGN KEY ("LibraryId") REFERENCES "Libraries" ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "Series" (
            "Id" TEXT PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Description" TEXT NULL,
            "Path" TEXT NOT NULL,
            "Status" TEXT NOT NULL DEFAULT 'Ready',
            "ThumbnailPath" TEXT NULL,
            "ThumbnailMeta" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            "DeletedAt" TEXT NULL,
            "LibraryId" TEXT NULL,
            FOREIGN KEY ("LibraryId") REFERENCES "Libraries" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "Media" (
            "Id" TEXT PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "SortName" TEXT NULL,
            "Size" INTEGER NOT NULL DEFAULT 0,
            "Extension" TEXT NOT NULL,
            "Pages" INTEGER NOT NULL DEFAULT 0,
            "Path" TEXT NOT NULL,
            "Status" TEXT NOT NULL DEFAULT 'Ready',
            "Hash" TEXT NULL,
            "KoreaderHash" TEXT NULL,
            "ThumbnailPath" TEXT NULL,
            "ThumbnailMeta" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            "ModifiedAt" TEXT NULL,
            "DeletedAt" TEXT NULL,
            "SeriesId" TEXT NULL,
            FOREIGN KEY ("SeriesId") REFERENCES "Series" ("Id") ON DELETE SET NULL
        );

        CREATE TABLE IF NOT EXISTS "SeriesMetadata" (
            "SeriesId" TEXT PRIMARY KEY,
            "AgeRating" INTEGER NULL,
            "Characters" TEXT NULL,
            "Booktype" TEXT NULL,
            "Comicid" INTEGER NULL,
            "ComicImage" TEXT NULL,
            "DescriptionFormatted" TEXT NULL,
            "Genres" TEXT NULL,
            "Imprint" TEXT NULL,
            "Links" TEXT NULL,
            "MetaType" TEXT NULL,
            "PublicationRun" TEXT NULL,
            "Publisher" TEXT NULL,
            "Status" TEXT NULL,
            "Summary" TEXT NULL,
            "Title" TEXT NULL,
            "TotalIssues" INTEGER NULL,
            "Volume" INTEGER NULL,
            "Writers" TEXT NULL,
            "Year" INTEGER NULL,
            FOREIGN KEY ("SeriesId") REFERENCES "Series" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "SeriesTags" (
            "SeriesId" TEXT NOT NULL,
            "TagId" INTEGER NOT NULL,
            PRIMARY KEY ("SeriesId", "TagId"),
            FOREIGN KEY ("SeriesId") REFERENCES "Series" ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("TagId") REFERENCES "Tags" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "EpubPageMaps" (
            "MediaId" TEXT NOT NULL,
            "ProfileKey" TEXT NOT NULL,
            "TotalPages" INTEGER NOT NULL,
            "FileModifiedAt" TEXT NOT NULL,
            "BuiltAt" TEXT NOT NULL,
            PRIMARY KEY ("MediaId", "ProfileKey"),
            FOREIGN KEY ("MediaId") REFERENCES "Media" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "MediaMetadata" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "MediaId" TEXT NULL,
            "AgeRating" INTEGER NULL,
            "Characters" TEXT NULL,
            "Colorists" TEXT NULL,
            "CoverArtists" TEXT NULL,
            "Format" TEXT NULL,
            "Day" INTEGER NULL,
            "Editors" TEXT NULL,
            "Genres" TEXT NULL,
            "IdentifierAmazon" TEXT NULL,
            "IdentifierCalibre" TEXT NULL,
            "IdentifierGoogle" TEXT NULL,
            "IdentifierIsbn" TEXT NULL,
            "IdentifierMobiAsin" TEXT NULL,
            "IdentifierUuid" TEXT NULL,
            "Inkers" TEXT NULL,
            "Language" TEXT NULL,
            "Letterers" TEXT NULL,
            "Links" TEXT NULL,
            "Month" INTEGER NULL,
            "Notes" TEXT NULL,
            "Number" TEXT NULL,
            "PageCount" INTEGER NULL,
            "Pencillers" TEXT NULL,
            "Publisher" TEXT NULL,
            "Series" TEXT NULL,
            "SeriesGroup" TEXT NULL,
            "StoryArc" TEXT NULL,
            "StoryArcNumber" TEXT NULL,
            "Summary" TEXT NULL,
            "Teams" TEXT NULL,
            "Title" TEXT NULL,
            "TitleSort" TEXT NULL,
            "Volume" INTEGER NULL,
            "Writers" TEXT NULL,
            "Year" INTEGER NULL,
            FOREIGN KEY ("MediaId") REFERENCES "Media" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "MediaPages" (
            "MediaId" TEXT NOT NULL,
            "Number" INTEGER NOT NULL,
            "FileName" TEXT NOT NULL,
            "MediaType" TEXT NOT NULL,
            "Width" INTEGER NULL,
            "Height" INTEGER NULL,
            "SizeBytes" INTEGER NULL,
            PRIMARY KEY ("MediaId", "Number"),
            FOREIGN KEY ("MediaId") REFERENCES "Media" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "MediaTags" (
            "MediaId" TEXT NOT NULL,
            "TagId" INTEGER NOT NULL,
            PRIMARY KEY ("MediaId", "TagId"),
            FOREIGN KEY ("MediaId") REFERENCES "Media" ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("TagId") REFERENCES "Tags" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "ReadingSessions" (
            "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "SessionDate" TEXT NOT NULL,
            "StartPage" INTEGER NULL,
            "EndPage" INTEGER NULL,
            "StartPercentage" TEXT NULL,
            "EndPercentage" TEXT NULL,
            "KoreaderProgress" TEXT NULL,
            "ElapsedSeconds" INTEGER NULL,
            "RenderedPage" INTEGER NULL,
            "RenderedTotalPages" INTEGER NULL,
            "RenderedProfileKey" TEXT NULL,
            "RenderedStatus" TEXT NULL,
            "ReadthroughNumber" INTEGER NOT NULL DEFAULT 0,
            "Status" TEXT NOT NULL DEFAULT 'Reading',
            "Notes" TEXT NULL,
            "MediaId" TEXT NOT NULL,
            "UserId" TEXT NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NULL,
            FOREIGN KEY ("MediaId") REFERENCES "Media" ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_AgeRestrictions_UserId" ON "AgeRestrictions" ("UserId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Libraries_ConfigId" ON "Libraries" ("ConfigId");
        CREATE INDEX IF NOT EXISTS "IX_LibraryExclusions_LibraryId" ON "LibraryExclusions" ("LibraryId");
        CREATE INDEX IF NOT EXISTS "IX_LibraryExclusions_UserId" ON "LibraryExclusions" ("UserId");
        CREATE INDEX IF NOT EXISTS "IX_Media_CreatedAt" ON "Media" ("CreatedAt");
        CREATE INDEX IF NOT EXISTS "IX_Media_DeletedAt" ON "Media" ("DeletedAt");
        CREATE INDEX IF NOT EXISTS "IX_Media_Hash" ON "Media" ("Hash");
        CREATE INDEX IF NOT EXISTS "IX_Media_KoreaderHash" ON "Media" ("KoreaderHash");
        CREATE INDEX IF NOT EXISTS "IX_Media_Path" ON "Media" ("Path");
        CREATE INDEX IF NOT EXISTS "IX_Media_SeriesId" ON "Media" ("SeriesId");
        CREATE INDEX IF NOT EXISTS "IX_Media_SeriesId_SortName" ON "Media" ("SeriesId", "SortName");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_MediaMetadata_MediaId" ON "MediaMetadata" ("MediaId");
        CREATE INDEX IF NOT EXISTS "IX_MediaTags_TagId" ON "MediaTags" ("TagId");
        CREATE INDEX IF NOT EXISTS "IX_ReadingSessions_MediaId" ON "ReadingSessions" ("MediaId");
        CREATE INDEX IF NOT EXISTS "IX_ReadingSessions_UserId_MediaId" ON "ReadingSessions" ("UserId", "MediaId");
        CREATE INDEX IF NOT EXISTS "IX_Series_LibraryId" ON "Series" ("LibraryId");
        CREATE INDEX IF NOT EXISTS "IX_Series_Path" ON "Series" ("Path");
        CREATE INDEX IF NOT EXISTS "IX_SeriesTags_TagId" ON "SeriesTags" ("TagId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tags_Name" ON "Tags" ("Name");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserPreferences_UserId" ON "UserPreferences" ("UserId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");
        """;

    public static void InitializeDatabase(this System.Data.Common.DbConnection connection)
    {
        connection.Execute(SchemaSql);
        UpgradeSchema(connection);
    }

    /// <summary>
    /// CREATE TABLE IF NOT EXISTS leaves an existing table as it was, so a column added later has
    /// to be added to databases that already hold data. Sessions already read as rendered pages
    /// keep the status they had, which until now was the only one, and get the percentage that
    /// page represents so the e-reader side starts from the same place.
    /// </summary>
    private static void UpgradeSchema(System.Data.Common.DbConnection connection)
    {
        const string hasColumnSql = """SELECT COUNT(*) FROM pragma_table_info('ReadingSessions') WHERE name = 'RenderedStatus';""";
        if (connection.ExecuteScalar<int>(hasColumnSql) > 0) return;

        connection.Execute("""ALTER TABLE "ReadingSessions" ADD COLUMN "RenderedStatus" TEXT NULL;""");
        connection.Execute("""UPDATE "ReadingSessions" SET "RenderedStatus" = "Status" WHERE "RenderedPage" IS NOT NULL;""");
        connection.Execute("""
            UPDATE "ReadingSessions"
            SET "EndPercentage" = printf('%.4f', 1.0 * "RenderedPage" / "RenderedTotalPages")
            WHERE "RenderedPage" IS NOT NULL AND "RenderedTotalPages" > 0 AND "EndPercentage" IS NULL;
            """);
    }

    public static async Task InitializeDatabaseAsync(
        this System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await connection.ExecuteAsync(SchemaSql);
        UpgradeSchema(connection);
    }

    public static async Task InitializeDatabaseAsync(
        this ISqliteConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await connectionFactory.CreateConnectionAsync(cancellationToken);
        await conn.ExecuteAsync(SchemaSql);
        UpgradeSchema(conn);
    }

    public static async Task<int> BackfillSortNamesAsync(
        this ISqliteConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await connectionFactory.CreateConnectionAsync(cancellationToken);
        const string selectSql = """SELECT "Id", "Name" FROM "Media" WHERE "SortName" IS NULL;""";
        var pending = (await conn.QueryAsync<IdNameRow>(
            selectSql)).ToList();

        if (pending.Count == 0) return 0;

        const string updateSql = """UPDATE "Media" SET "SortName" = @SortName WHERE "Id" = @Id;""";
        var updates = pending.Select(p => new
        {
            Id = p.Id,
            SortName = DiarSpeicher.Core.Filesystem.SortKey.From(p.Name)
        });

        await conn.ExecuteAsync(updateSql, updates);
        return pending.Count;
    }
}
