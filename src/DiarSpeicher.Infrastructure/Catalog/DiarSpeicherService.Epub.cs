using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data.Extensions;

namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Lectura del interior de un EPUB para el lector: indice de contenidos y recursos sueltos.
/// </summary>
public sealed partial class DiarSpeicherService
{
    private static async Task<List<DiarSpeicherEpubTocItem>> ReadNcxTocAsync(ZipArchive archive, CancellationToken ct)
    {
        var tocItems = new List<DiarSpeicherEpubTocItem>();

        var ncxEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));
        if (ncxEntry == null) return tocItems;

        using var stream = ncxEntry.Open();
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
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "s", "sm");
        parameters.Add("MediaId", mediaId);
        var sql = $"""
            SELECT m."Id", m."Name", m."Path", m."Extension",
                   mm."Id", mm."Title"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @MediaId AND {where}
            LIMIT 1;
            """;
        var rows = await conn.QueryAsync<Media, MediaMetadata?, Media>(
            new CommandDefinition(sql, parameters, cancellationToken: ct),
            (m, mm) => { m.Metadata = mm; return m; },
            splitOn: "Id");
        var media = rows.FirstOrDefault();

        if (media == null || !File.Exists(media.Path)) return null;

        var ext = media.Extension.TrimStart('.').ToLowerInvariant();
        if (ext != "epub") return null;

        using var fileStream = new FileStream(media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
        var tocItems = await ReadNcxTocAsync(archive, ct);
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

    public async Task<(byte[] Data, string ContentType)?> GetEpubResourceAsync(AuthUser user, string mediaId, string resourcePath, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "s", "sm");
        parameters.Add("MediaId", mediaId);
        var sql = $"""
            SELECT m."Id", m."Path"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @MediaId AND {where}
            LIMIT 1;
            """;
        var media = await conn.QueryFirstOrDefaultAsync<Media>(new CommandDefinition(sql, parameters, cancellationToken: ct));

        if (media == null || !File.Exists(media.Path)) return null;

        var cleanPath = resourcePath.TrimStart('/').Split('#')[0];

        using var fileStream = new FileStream(media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(cleanPath, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(Path.GetFileName(cleanPath), StringComparison.OrdinalIgnoreCase));

        if (entry == null) return null;

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        var contentType = GetEpubResourceContentType(Path.GetExtension(entry.FullName));
        return (ms.ToArray(), contentType);
    }

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
