using System.IO.Compression;
using System.Xml.Linq;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.StumpV2;

public sealed class StumpV2Service : IStumpV2Service
{
    private readonly DiarSpeicherDbContext _db;
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly IScannerQueue _scannerQueue;
    private readonly ILogger<StumpV2Service> _logger;
    private readonly UploadOptions _uploadOptions;

    public StumpV2Service(
        DiarSpeicherDbContext db,
        ICompositeBookProcessor bookProcessor,
        IScannerQueue scannerQueue,
        ILogger<StumpV2Service> logger,
        IOptions<StorageOptions> storageOptions)
    {
        _db = db;
        _bookProcessor = bookProcessor;
        _scannerQueue = scannerQueue;
        _logger = logger;
        _uploadOptions = storageOptions.Value.Upload;
    }

    public async Task<StumpPageResponse<StumpMediaDto>> GetMediaAsync(AuthUser user, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .OrderBy(m => m.Id);

        var total = await query.CountAsync(ct);
        var mediaList = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);

        var mediaIds = mediaList.Select(m => m.Id).ToList();
        var sessionMap = await _db.GetLatestSessionsPerMediaAsync(user.Id, mediaIds, ct);

        var dtos = mediaList.Select(m => ToMediaDto(m, sessionMap.GetValueOrDefault(m.Id))).ToList();

        return new StumpPageResponse<StumpMediaDto>
        {
            Data = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<StumpMediaDto?> GetMediaByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media == null) return null;

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        return ToMediaDto(media, session);
    }

    public async Task<List<StumpMediaDto>> GetKeepReadingAsync(AuthUser user, CancellationToken ct = default)
    {
        var sessions = await _db.GetKeepReadingSessionsAsync(user.Id, ct);

        var mediaIds = sessions.Select(s => s.MediaId).Distinct().ToList();

        var mediaList = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m => mediaIds.Contains(m.Id))
            .ToListAsync(ct);

        var mediaMap = mediaList.ToDictionary(m => m.Id);
        var sessionMap = sessions.ToDictionary(s => s.MediaId);

        var result = new List<StumpMediaDto>();
        foreach (var id in mediaIds)
        {
            if (mediaMap.TryGetValue(id, out var media))
            {
                result.Add(ToMediaDto(media, sessionMap.GetValueOrDefault(id)));
            }
        }

        return result;
    }

    public async Task<StumpPageResponse<StumpSeriesDto>> GetSeriesAsync(AuthUser user, string? libraryId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        var query = _db.Series.ForUser(user).Include(s => s.Metadata).Include(s => s.Media).AsQueryable();

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            query = query.Where(s => s.LibraryId == libraryId);
        }

        query = query.OrderBy(s => s.Name);

        var total = await query.CountAsync(ct);
        var seriesList = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);

        var dtos = seriesList.Select(ToSeriesDto).ToList();

        return new StumpPageResponse<StumpSeriesDto>
        {
            Data = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<StumpSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Include(s => s.Media)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return series == null ? null : ToSeriesDto(series);
    }

    public async Task<StumpPageResponse<StumpMediaDto>> GetSeriesMediaAsync(AuthUser user, string seriesId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m => m.SeriesId == seriesId)
            .OrderBy(m => m.Name);

        var total = await query.CountAsync(ct);
        var mediaList = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);

        var mediaIds = mediaList.Select(m => m.Id).ToList();
        var sessionMap = await _db.GetLatestSessionsPerMediaAsync(user.Id, mediaIds, ct);

        var dtos = mediaList.Select(m => ToMediaDto(m, sessionMap.GetValueOrDefault(m.Id))).ToList();

        return new StumpPageResponse<StumpMediaDto>
        {
            Data = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<List<StumpLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default)
    {
        var libraries = await _db.Libraries.ForUser(user)
            .Include(l => l.Series)
            .Include(l => l.Config)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        var mediaCounts = await _db.Media
            .Where(m => m.Series != null && m.Series.LibraryId != null)
            .GroupBy(m => m.Series!.LibraryId!)
            .Select(g => new { LibraryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LibraryId, x => x.Count, ct);

        return libraries
            .Select(l => ToLibraryDto(l, mediaCounts.GetValueOrDefault(l.Id)))
            .ToList();
    }

    public async Task<StumpLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var lib = await _db.Libraries.ForUser(user)
            .Include(l => l.Series)
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (lib == null) return null;

        var mediaCount = await _db.Media
            .CountAsync(m => m.Series != null && m.Series.LibraryId == lib.Id, ct);

        return ToLibraryDto(lib, mediaCount);
    }

    public async Task<bool> TriggerLibraryScanAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        var lib = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == libraryId, ct);

        if (lib == null)
        {
            return false;
        }

        await _scannerQueue.QueueScanAsync(new ScanRequest(libraryId), ct);
        _logger.LogInformation("Enqueued scan task for library {LibraryId} by user {UserId}", libraryId, user.Id);
        return true;
    }

    public async Task<ExtractedPage?> GetMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var extracted = await _bookProcessor.ExtractPageAsync(media.Path, page, ct);

        if (extracted != null)
        {
            await TrackReadingProgressAsync(user.Id, mediaId, page, media.Pages, ct);
        }

        return extracted;
    }

    public async Task<(string Path, string ContentType)?> GetMediaFileAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        return (media.Path, ContentTypeExtensions.FromExtension(media.Extension).ToMimeType());
    }

    public async Task<bool> UpdateProgressAsync(AuthUser user, string mediaId, StumpUpdateProgressInput input, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null) return false;

        // The auth middleware synthesises a "default-owner" identity while the server has
        // no users yet. That id has no row in Users, so writing a reading session would
        // violate the foreign key.
        var userExists = await _db.Users.AnyAsync(u => u.Id == user.Id, ct);
        if (!userExists) return false;

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == mediaId)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = mediaId,
                StartPage = 1,
                StartPercentage = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ReadingSessions.Add(session);
        }

        session.EndPage = input.Page;
        if (input.Percentage.HasValue)
        {
            session.EndPercentage = (decimal)input.Percentage.Value;
        }
        else if (media.Pages > 0)
        {
            session.EndPercentage = Math.Clamp((decimal)input.Page / media.Pages, 0m, 1m);
        }

        var isCompleted = input.IsCompleted ?? (session.EndPercentage >= 1.0m || input.Page >= media.Pages);
        session.Status = isCompleted ? ReadingStatus.Finished : ReadingStatus.Reading;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StumpEpubTocDto?> GetEpubTocAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var ext = media.Extension.TrimStart('.').ToLowerInvariant();
        if (ext != "epub") return null;

        await using var fileStream = new FileStream(media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var archive = await ZipArchive.CreateAsync(fileStream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, ct);
        var tocItems = new List<StumpEpubTocItem>();

        var ncxEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));
        if (ncxEntry != null)
        {
            await using var stream = await ncxEntry.OpenAsync(ct);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            var navPoints = doc.Descendants().Where(e => e.Name.LocalName == "navPoint");

            foreach (var point in navPoints)
            {
                var label = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value.Trim();
                var contentSrc = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;

                if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(contentSrc))
                {
                    tocItems.Add(new StumpEpubTocItem { Title = label, Href = contentSrc });
                }
            }
        }

        if (tocItems.Count == 0)
        {
            var htmlEntries = archive.Entries
                .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, NaturalSortComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < htmlEntries.Count; i++)
            {
                tocItems.Add(new StumpEpubTocItem { Title = $"Section {i + 1}", Href = htmlEntries[i].FullName });
            }
        }

        return new StumpEpubTocDto
        {
            MediaId = media.Id,
            Title = media.Metadata?.Title ?? media.Name,
            Items = tocItems
        };
    }

    public async Task<(byte[] Data, string ContentType)?> GetEpubResourceAsync(AuthUser user, string mediaId, string resourcePath, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var cleanPath = resourcePath.TrimStart('/').Split('#')[0];

        await using var fileStream = new FileStream(media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var archive = await ZipArchive.CreateAsync(fileStream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, ct);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(cleanPath, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(Path.GetFileName(cleanPath), StringComparison.OrdinalIgnoreCase));

        if (entry == null) return null;

        await using var stream = await entry.OpenAsync(ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".html" or ".xhtml" => "application/xhtml+xml",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".ncx" => "application/x-dtbncx+xml",
            ".opf" => "application/oebps-package+xml",
            _ => "application/octet-stream"
        };

        return (ms.ToArray(), contentType);
    }

    public async Task<StumpLibraryDto?> CreateLibraryAsync(AuthUser user, StumpCreateLibraryInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Path))
            return null;

        var fullPath = Path.GetFullPath(input.Path);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        var library = new Library
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name.Trim(),
            Path = fullPath,
            Description = input.Description?.Trim(),
            Emoji = input.Emoji?.Trim(),
            Status = FileStatus.Ready,
            Config = BuildConfig(input.Config)
        };

        _db.Libraries.Add(library);
        await _db.SaveChangesAsync(ct);

        return ToLibraryDto(library, 0);
    }

    public async Task<StumpLibraryDto?> UpdateLibraryAsync(AuthUser user, string id, StumpUpdateLibraryInput input, CancellationToken ct = default)
    {
        var library = await _db.Libraries.ForUser(user)
            .Include(l => l.Series)
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (library == null) return null;

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            library.Name = input.Name.Trim();
        }

        if (input.Description != null)
        {
            library.Description = input.Description.Trim();
        }

        if (input.Emoji != null)
        {
            library.Emoji = input.Emoji.Trim();
        }

        if (input.Config != null)
        {
            ApplyConfig(library.Config, input.Config);
        }

        library.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var mediaCount = await _db.Media
            .CountAsync(m => m.Series != null && m.Series.LibraryId == library.Id, ct);

        return ToLibraryDto(library, mediaCount);
    }

    /// <summary>
    /// Borra el registro de la biblioteca, nunca los ficheros del disco: DiarSpeicher indexa
    /// carpetas que no son suyas y que pueden estar compartidas con otros programas.
    /// </summary>
    public async Task<bool> DeleteLibraryAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var library = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (library == null) return false;

        _db.Libraries.Remove(library);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static LibraryConfig BuildConfig(StumpLibraryConfigDto? dto)
    {
        var config = new LibraryConfig { LibraryPattern = LibraryPattern.SeriesBased };
        if (dto != null)
        {
            ApplyConfig(config, dto);
        }

        return config;
    }

    private static void ApplyConfig(LibraryConfig config, StumpLibraryConfigDto dto)
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

    private static StumpLibraryDto ToLibraryDto(Library library, int mediaCount) => new()
    {
        Id = library.Id,
        Name = library.Name,
        Path = library.Path,
        Status = library.Status.ToString().ToUpperInvariant(),
        SeriesCount = library.Series?.Count ?? 0,
        MediaCount = mediaCount,
        Description = library.Description,
        Emoji = library.Emoji,
        CreatedAt = library.CreatedAt,
        UpdatedAt = library.UpdatedAt,
        LastScannedAt = library.LastScannedAt,
        Config = library.Config == null ? null : new StumpLibraryConfigDto
        {
            LibraryType = library.Config.LibraryType.ToString(),
            LibraryPattern = library.Config.LibraryPattern.ToString(),
            DefaultReadingDir = library.Config.DefaultReadingDir.ToString(),
            DefaultReadingMode = library.Config.DefaultReadingMode.ToString(),
            DefaultLibraryViewMode = library.Config.DefaultLibraryViewMode.ToString(),
            ConvertRarToZip = library.Config.ConvertRarToZip,
            HardDeleteConversions = library.Config.HardDeleteConversions,
            GenerateFileHashes = library.Config.GenerateFileHashes,
            GenerateKoreaderHashes = library.Config.GenerateKoreaderHashes,
            ProcessMetadata = library.Config.ProcessMetadata,
            Watch = library.Config.Watch,
            HideSeriesView = library.Config.HideSeriesView,
            ThumbnailWidth = library.Config.ThumbnailWidth,
            ThumbnailHeight = library.Config.ThumbnailHeight,
            IgnoreRules = library.Config.IgnoreRules
        }
    };

    public Task<UploadResult> UploadToLibraryAsync(
        AuthUser user,
        string libraryId,
        string? subpath,
        IEnumerable<StumpUploadFileInput> files,
        CancellationToken ct = default)
    {
        return UploadToLibraryAsync(user, libraryId, subpath, ToAsync(files), ct);

        static async IAsyncEnumerable<StumpUploadFileInput> ToAsync(IEnumerable<StumpUploadFileInput> source)
        {
            foreach (var item in source)
            {
                yield return item;
            }
        }
    }

    public async Task<UploadResult> UploadToLibraryAsync(
        AuthUser user,
        string libraryId,
        string? subpath,
        IAsyncEnumerable<StumpUploadFileInput> files,
        CancellationToken ct = default)
    {
        if (!_uploadOptions.EnableUpload)
        {
            return UploadResult.Fail(UploadOutcome.UploadDisabled, "Uploads are disabled on this server.");
        }

        if (!user.HasPermission(Permissions.FileUpload))
        {
            return UploadResult.Fail(UploadOutcome.PermissionDenied, "This account is not allowed to upload files.");
        }

        var library = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == libraryId, ct);

        if (library == null || !Directory.Exists(library.Path))
        {
            return UploadResult.Fail(UploadOutcome.LibraryNotFound, "Library not found.");
        }

        if (!TryResolveTargetDirectory(library.Path, subpath, out var fullTargetDir))
        {
            return UploadResult.Fail(UploadOutcome.LibraryNotFound, "Library not found.");
        }

        // Subir a una subcarpeta que ya existe no exige poder crearlas.
        if (!Directory.Exists(fullTargetDir) && !user.HasPermission(Permissions.CreateFolder))
        {
            return UploadResult.Fail(
                UploadOutcome.PermissionDenied,
                "This account is not allowed to create folders in the library.");
        }

        Directory.CreateDirectory(fullTargetDir);

        var savedFiles = new List<UploadedFileDto>();
        await foreach (var file in files.WithCancellation(ct))
        {
            if (string.IsNullOrWhiteSpace(file.FileName) || file.Content == null) continue;

            if (!_uploadOptions.IsExtensionAllowed(file.FileName))
            {
                return UploadResult.Fail(
                    UploadOutcome.ExtensionNotAllowed,
                    $"The extension of '{Path.GetFileName(file.FileName)}' is not accepted.");
            }

            var safeName = Path.GetFileName(file.FileName);
            var destPath = Path.Combine(fullTargetDir, safeName);

            long written;
            try
            {
                written = await WriteWithLimitAsync(file.Content, destPath, _uploadOptions.MaxFileUploadSize, ct);
            }
            catch (UploadTooLargeException)
            {
                DeleteQuietly(destPath);
                return UploadResult.Fail(
                    UploadOutcome.FileTooLarge,
                    $"'{safeName}' exceeds the maximum size of {_uploadOptions.MaxFileUploadSize} bytes.");
            }

            savedFiles.Add(new UploadedFileDto
            {
                Name = safeName,
                Path = destPath,
                Size = written
            });
        }

        if (savedFiles.Count == 0)
        {
            return UploadResult.Fail(UploadOutcome.NoAcceptedFiles, "No valid files were provided.");
        }

        // Auto-enqueue scan
        await _scannerQueue.QueueScanAsync(new ScanRequest(library.Id), ct);

        return UploadResult.Ok(new StumpUploadResponseDto
        {
            UploadedCount = savedFiles.Count,
            Files = savedFiles,
            ScanJobTriggered = true
        });
    }

    private bool TryResolveTargetDirectory(
        string libraryPath,
        string? subpath,
        out string fullTargetDir)
    {
        var cleanSubpath = (subpath ?? string.Empty).Trim('/', '\\');
        var targetDir = string.IsNullOrEmpty(cleanSubpath)
            ? libraryPath
            : Path.Combine(libraryPath, cleanSubpath);

        fullTargetDir = Path.GetFullPath(targetDir);
        var fullLibraryPath = Path.GetFullPath(libraryPath);

        // Path traversal protection. The library prefix is compared with a trailing
        // separator, otherwise a subpath of "../libFoo" escapes into any sibling
        // directory whose name merely starts with the library's name.
        var libraryPrefix = fullLibraryPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullLibraryPath
            : fullLibraryPath + Path.DirectorySeparatorChar;

        var isInsideLibrary =
            fullTargetDir.Equals(fullLibraryPath, StringComparison.OrdinalIgnoreCase) ||
            fullTargetDir.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase);

        if (!isInsideLibrary)
        {
            _logger.LogWarning("Attempted path traversal upload to {TargetDir} outside {LibraryPath}", fullTargetDir, fullLibraryPath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Copies the stream while enforcing <paramref name="maxBytes"/> as it goes, so an
    /// oversized upload is aborted mid-stream instead of after the whole file has landed
    /// on disk. The caller removes the partial file.
    /// </summary>
    private static async Task<long> WriteWithLimitAsync(Stream source, string destPath, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;

        await using var fs = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new UploadTooLargeException();
            }

            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: ignore failures if the file is locked or permissions prevent deletion
        }
    }

    public async Task<StumpSystemStatusDto> GetSystemStatusAsync(CancellationToken ct = default)
    {
        var userCount = await _db.Users.CountAsync(ct);
        return new StumpSystemStatusDto
        {
            Status = "OK",
            Semver = "0.1.0",
            IsClaimed = userCount > 0
        };
    }

    private async Task TrackReadingProgressAsync(string userId, string mediaId, int page, int totalPages, CancellationToken ct)
    {
        try
        {
        // The auth middleware synthesises a "default-owner" identity while the server has
        // no users yet. That id has no row in Users, so writing a reading session would
        // violate the foreign key.
            var userExists = await _db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists) return;

            var session = await _db.ReadingSessions
                .Where(s => s.UserId == userId && s.MediaId == mediaId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);

            if (session == null)
            {
                session = new ReadingSession
                {
                    UserId = userId,
                    MediaId = mediaId,
                    StartPage = page,
                    StartPercentage = totalPages > 0 ? Math.Clamp((decimal)page / totalPages, 0m, 1m) : 0,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.ReadingSessions.Add(session);
            }

            session.EndPage = page;
            session.EndPercentage = totalPages > 0 ? Math.Clamp((decimal)page / totalPages, 0m, 1m) : 0;
            session.Status = (totalPages > 0 && page >= totalPages) ? ReadingStatus.Finished : ReadingStatus.Reading;
            session.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update reading session for user {UserId}, media {MediaId}", userId, mediaId);
        }
    }

    private static StumpMediaDto ToMediaDto(Media m, ReadingSession? session)
    {
        return new StumpMediaDto
        {
            Id = m.Id,
            Name = m.Name,
            Size = m.Size,
            Extension = m.Extension,
            Pages = m.Pages,
            Status = m.Status.ToString(),
            Hash = m.Hash,
            KoreaderHash = m.KoreaderHash,
            Path = m.Path,
            SeriesId = m.SeriesId,
            CreatedAt = m.CreatedAt,
            CurrentPage = session?.EndPage,
            IsCompleted = session?.Status == ReadingStatus.Finished,
            Metadata = m.Metadata != null ? new StumpMediaMetadataDto
            {
                Title = m.Metadata.Title,
                Summary = m.Metadata.Summary,
                Writers = m.Metadata.Writers,
                Genre = m.Metadata.Genres,
                Publisher = m.Metadata.Publisher,
                AgeRating = m.Metadata.AgeRating,
                Number = (float?)m.Metadata.Number
            } : null
        };
    }

    private static StumpSeriesDto ToSeriesDto(Series s)
    {
        return new StumpSeriesDto
        {
            Id = s.Id,
            Name = s.Name,
            Path = s.Path,
            Status = s.Status.ToString(),
            LibraryId = s.LibraryId ?? string.Empty,
            MediaCount = s.Media.Count,
            Description = s.Description ?? s.Metadata?.Summary
        };
    }
}

public sealed class UploadTooLargeException : Exception;
