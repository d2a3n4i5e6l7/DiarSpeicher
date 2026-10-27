using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    private static ZipArchiveEntry? FindNcxEntry(ZipArchive archive) =>
        archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));

    private static async Task<List<DiarSpeicherEpubTocItem>> ReadNcxTocAsync(ZipArchiveEntry? ncxEntry, CancellationToken ct)
    {
        var tocItems = new List<DiarSpeicherEpubTocItem>();
        if (ncxEntry == null) return tocItems;

        await using var stream = await ncxEntry.OpenAsync(ct);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        foreach (var point in doc.Descendants().Where(e => e.Name.LocalName == "navPoint"))
        {
            var label = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value.Trim();
            var contentSrc = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;

            if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(contentSrc))
            {
                tocItems.Add(new DiarSpeicherEpubTocItem { Title = label, Href = contentSrc });
            }
        }

        return tocItems;
    }

    private static List<DiarSpeicherEpubTocItem> BuildSequentialToc(ZipArchive archive)
    {
        var htmlEntries = archive.Entries
            .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, NaturalSortComparer.OrdinalIgnoreCase)
            .ToList();

        return [.. htmlEntries.Select((e, i) => new DiarSpeicherEpubTocItem { Title = $"Section {i + 1}", Href = e.FullName })];
    }

    public async Task<DiarSpeicherEpubTocDto?> GetEpubTocAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.MediaId = mediaId;
        const string sql = $"""
            SELECT m."Id", m."Name", m."Path", m."Extension"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @MediaId AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;
        var rows = await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn);
        var media = rows.FirstOrDefault();

        if (media == null || !File.Exists(media.Path) || !IsEpub(media)) return null;

        using var archive = OpenEpubArchive(media.Path);
        var tocItems = await ReadNcxTocAsync(FindNcxEntry(archive), ct);
        if (tocItems.Count == 0)
        {
            tocItems = BuildSequentialToc(archive);
        }

        return new DiarSpeicherEpubTocDto
        {
            MediaId = media.Id,
            Title = media.Metadata?.Title ?? media.Name,
            Items = tocItems
        };
    }

    public async Task<DiarSpeicherEpubManifestDto?> GetEpubManifestAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        var media = await FindReadableMediaAsync(user, mediaId, ct);
        if (media == null || !IsEpub(media)) return null;

        using var archive = OpenEpubArchive(media.Path);
        var spine = await EpubOpfReader.GetSpineEntriesAsync(archive, ct);
        return new DiarSpeicherEpubManifestDto
        {
            ChapterSizes = [.. spine.Select(entry => entry.Length)],
            Toc = await BuildChapterTocAsync(archive, spine, ct),
        };
    }

    public async Task<DiarSpeicherEpubChapterDto?> GetEpubChapterAsync(AuthUser user, string mediaId, int chapter, string resourceBase, CancellationToken ct = default)
    {
        var media = await FindReadableMediaAsync(user, mediaId, ct);
        if (media == null || !IsEpub(media)) return null;

        using var archive = OpenEpubArchive(media.Path);
        var spine = await EpubOpfReader.GetSpineEntriesAsync(archive, ct);
        if (chapter < 0 || chapter >= spine.Count) return null;

        var entry = spine[chapter];
        await using var stream = await entry.OpenAsync(ct);
        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync(ct);
        return new DiarSpeicherEpubChapterDto
        {
            Html = EpubChapterSanitizer.Sanitize(html, entry.FullName, resourceBase, ContentVersion(media)),
        };
    }

    private static async Task<List<DiarSpeicherEpubTocEntry>> BuildChapterTocAsync(ZipArchive archive, List<ZipArchiveEntry> spine, CancellationToken ct)
    {
        var ncxEntry = FindNcxEntry(archive);
        var ncxDirectory = EpubPaths.DirectoryOf(ncxEntry?.FullName ?? string.Empty);
        var toc = new List<DiarSpeicherEpubTocEntry>();
        foreach (var item in await ReadNcxTocAsync(ncxEntry, ct))
        {
            var (path, fragment) = EpubPaths.Resolve(ncxDirectory, item.Href);
            var chapter = spine.FindIndex(entry => entry.FullName.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (chapter >= 0)
            {
                toc.Add(new DiarSpeicherEpubTocEntry
                {
                    Title = item.Title,
                    Chapter = chapter,
                    Anchor = fragment is null ? null : EpubChapterSanitizer.AnchorPrefix + fragment,
                });
            }
        }

        return toc.Count > 0
            ? toc
            : [.. spine.Select((_, index) => new DiarSpeicherEpubTocEntry { Title = $"Sección {index + 1}", Chapter = index })];
    }

    public async Task<(byte[] Data, string ContentType)?> GetEpubResourceAsync(AuthUser user, string mediaId, string resourcePath, CancellationToken ct = default)
    {
        var media = await FindReadableMediaAsync(user, mediaId, ct);
        if (media == null) return null;

        var cleanPath = resourcePath.TrimStart('/').Split('#')[0];

        using var archive = OpenEpubArchive(media.Path);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(cleanPath, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(Path.GetFileName(cleanPath), StringComparison.OrdinalIgnoreCase));

        if (entry == null) return null;

        await using var stream = await entry.OpenAsync(ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        var contentType = GetEpubResourceContentType(Path.GetExtension(entry.FullName));
        return (ms.ToArray(), contentType);
    }

    private async Task<Media?> FindReadableMediaAsync(AuthUser user, string mediaId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.MediaId = mediaId;
        const string sql = $"""
            SELECT m."Id", m."Path", m."Extension", m."Size", m."CreatedAt", m."ModifiedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @MediaId AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;
        var media = await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);
        return media != null && File.Exists(media.Path) ? media : null;
    }

    private static bool IsEpub(Media media) =>
        media.Extension.TrimStart('.').Equals("epub", StringComparison.OrdinalIgnoreCase);

    private static ZipArchive OpenEpubArchive(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true), ZipArchiveMode.Read, leaveOpen: false);

    private static string ContentVersion(Media media) =>
        $"{media.Size:x}{(media.ModifiedAt ?? media.CreatedAt).ToUnixTimeSeconds():x}";

    private static readonly FrozenDictionary<string, string> EpubResourceMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "application/xhtml+xml",
            [".xhtml"] = "application/xhtml+xml",
            [".css"] = "text/css",
            [".js"] = "application/javascript",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".ncx"] = "application/x-dtbncx+xml",
            [".opf"] = "application/oebps-package+xml",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static string GetEpubResourceContentType(string extension) =>
        EpubResourceMimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
}
