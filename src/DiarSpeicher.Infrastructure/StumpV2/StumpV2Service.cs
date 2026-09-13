using System.Globalization;
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
    private readonly StorageOptions _storage;
    private readonly LibraryRootsOptions _libraryRoots;
    private readonly ITrashService _trash;

    /// <summary>Lo unico que se acepta como portada subida a mano.</summary>
    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public StumpV2Service(
        DiarSpeicherDbContext db,
        ICompositeBookProcessor bookProcessor,
        IScannerQueue scannerQueue,
        ILogger<StumpV2Service> logger,
        IOptions<StorageOptions> storageOptions,
        IOptions<LibraryRootsOptions> libraryRoots,
        ITrashService trash)
    {
        _db = db;
        _bookProcessor = bookProcessor;
        _scannerQueue = scannerQueue;
        _logger = logger;
        _uploadOptions = storageOptions.Value.Upload;
        _storage = storageOptions.Value;
        _libraryRoots = libraryRoots.Value;
        _trash = trash;
    }

    /// <summary>
    /// Portada de la serie: la elegida a mano si la hay, y si no la del primer tomo por
    /// orden natural, que es lo que hacia el unico endpoint que existia hasta ahora.
    /// </summary>
    public async Task<(byte[] Data, string ContentType)?> GetSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return null;

        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath) && File.Exists(series.ThumbnailPath))
        {
            return await ReadImageAsync(series.ThumbnailPath, ct);
        }

        var fallback = await _db.Media.ForUser(user)
            .Where(m => m.SeriesId == seriesId && m.ThumbnailPath != null)
            .OrderBy(m => m.Name)
            .Select(m => m.ThumbnailPath)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(fallback) || !File.Exists(fallback)
            ? null
            : await ReadImageAsync(fallback, ct);
    }

    /// <summary>
    /// Toma como portada la miniatura de un tomo que ya esta indexado. Se copia en vez de
    /// apuntar al fichero del tomo: si ese tomo se borra o se reescanea, la portada de la
    /// serie no debe irse con el.
    /// </summary>
    public async Task<bool> SetSeriesThumbnailFromMediaAsync(AuthUser user, string seriesId, string mediaId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        var source = await _db.Media.ForUser(user)
            .Where(m => m.Id == mediaId)
            .Select(m => m.ThumbnailPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return false;

        var target = BuildSeriesThumbnailPath(seriesId, Path.GetExtension(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);

        await ApplySeriesThumbnailAsync(series, target, ct);
        return true;
    }

    public async Task<bool> SetSeriesThumbnailAsync(AuthUser user, string seriesId, Stream image, string fileName, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!CoverExtensions.Contains(extension)) return false;

        var target = BuildSeriesThumbnailPath(seriesId, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await image.CopyToAsync(output, ct);
        }

        await ApplySeriesThumbnailAsync(series, target, ct);
        return true;
    }

    /// <summary>Vuelve a la portada automatica: se borra la elegida y manda el primer tomo.</summary>
    public async Task<bool> ClearSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath) && File.Exists(series.ThumbnailPath))
        {
            TryDeleteFile(series.ThumbnailPath);
        }

        series.ThumbnailPath = null;
        series.ThumbnailMeta = null;
        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// ThumbnailMeta guarda la marca de tiempo: el navegador cachea la portada por URL, y
    /// sin algo que cambie en ella una portada nueva no se veria hasta vaciar la cache.
    /// </summary>
    private async Task ApplySeriesThumbnailAsync(Series series, string path, CancellationToken ct)
    {
        // Una portada anterior con otra extension quedaria huerfana en disco.
        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath)
            && !string.Equals(series.ThumbnailPath, path, StringComparison.Ordinal)
            && File.Exists(series.ThumbnailPath))
        {
            TryDeleteFile(series.ThumbnailPath);
        }

        series.ThumbnailPath = path;
        series.ThumbnailMeta = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private string BuildSeriesThumbnailPath(string seriesId, string extension) =>
        Path.Combine(_storage.ResolveThumbnailsPath(), "series", $"{seriesId}{extension}");

    private static async Task<(byte[] Data, string ContentType)> ReadImageAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var mime = ContentTypeExtensions.FromExtension(Path.GetExtension(path)).MimeType();
        return (bytes, mime);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "No se pudo borrar la miniatura {Path}", path);
        }
    }

    public async Task<StumpPageResponse<StumpMediaDto>> GetMediaAsync(
        AuthUser user,
        int page,
        int pageSize,
        bool newestFirst = false,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        // El Id es un ULID, monotono por instante de creacion, asi que ordenar por el es
        // ordenar por antiguedad sin necesidad de mirar CreatedAt.
        var ordered = _db.Media.ForUser(user).Include(m => m.Metadata);
        var query = newestFirst
            ? ordered.OrderByDescending(m => m.Id)
            : ordered.OrderBy(m => m.Id);

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

    /// <summary>
    /// Renombra o describe una serie. ForUser va delante a proposito: sin el, cualquiera
    /// con permiso de biblioteca podria editar una serie que su filtro de edad le oculta.
    /// </summary>
    public async Task<StumpSeriesDto?> UpdateSeriesAsync(AuthUser user, string id, StumpUpdateSeriesInput input, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Include(s => s.Media)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (series == null) return null;

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            series.Name = input.Name.Trim();
        }

        // La descripcion si admite vaciarse: mandar cadena vacia la borra, nulo no la toca.
        if (input.Description != null)
        {
            series.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        }

        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToSeriesDto(series);
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

        // Cuenta de lo que quedo sin carpeta, para poder avisar en la tarjeta sin que el
        // cliente tenga que preguntar biblioteca por biblioteca.
        var missingCounts = await _db.Media
            .Where(m => m.Status == FileStatus.Missing && m.Series != null && m.Series.LibraryId != null)
            .GroupBy(m => m.Series!.LibraryId!)
            .Select(g => new { LibraryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LibraryId, x => x.Count, ct);

        return libraries
            .Select(l =>
            {
                var dto = ToLibraryDto(l, mediaCounts.GetValueOrDefault(l.Id));
                dto.MissingSeries = l.Series.Count(s => s.Status == FileStatus.Missing);
                dto.MissingVolumes = missingCounts.GetValueOrDefault(l.Id);

                return dto;
            })
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

        if (!_libraryRoots.TryResolve(input.Path, out var fullPath))
        {
            throw new InvalidOperationException(
                "Esa ruta esta fuera de las carpetas permitidas para bibliotecas.");
        }

        // Una raiz es donde viven las bibliotecas, no una biblioteca. Registrarla deja una
        // entrada imposible de retirar del disco: borrar esa carpeta arrastraria todas las
        // demas que cuelgan de ella.
        if (_libraryRoots.IsRoot(fullPath))
        {
            throw new InvalidOperationException(
                "Esa ruta es una raiz de bibliotecas. Elige o crea una carpeta dentro de ella.");
        }

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

        // Una biblioteca recien dada de alta esta vacia hasta que alguien la escanea, y
        // nadie espera tener que pedirlo: la carpeta ya se eligio al crearla.
        await _scannerQueue.QueueScanAsync(new ScanRequest(library.Id), ct);

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

        var oldPath = library.Path;
        var newPath = ResolveLibraryMove(library, input.Path);

        library.UpdatedAt = DateTimeOffset.UtcNow;

        if (newPath == null)
        {
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            library.Path = newPath;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.SaveChangesAsync(ct);
            await RepointLibraryPathsAsync(library.Id, oldPath, newPath, ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Biblioteca {Library} movida de {OldPath} a {NewPath}", library.Id, oldPath, newPath);
        }

        var mediaCount = await _db.Media
            .CountAsync(m => m.Series != null && m.Series.LibraryId == library.Id, ct);

        return ToLibraryDto(library, mediaCount);
    }

    /// <summary>
    /// Valida la carpeta destino. Null cuando no hay nada que mover; lanza con el motivo
    /// cuando la ruta no sirve, para que el cliente pueda corregirla.
    /// </summary>
    private string? ResolveLibraryMove(Library library, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;

        if (!_libraryRoots.TryResolve(requested, out var resolved))
        {
            throw new InvalidOperationException(
                "Esa ruta esta fuera de las carpetas permitidas para bibliotecas.");
        }

        if (_libraryRoots.IsRoot(resolved))
        {
            throw new InvalidOperationException(
                "Esa ruta es una raiz de bibliotecas. Elige o crea una carpeta dentro de ella.");
        }

        if (string.Equals(resolved, library.Path, StringComparison.Ordinal)) return null;

        // Al crear se acepta una carpeta que no existe y se crea. Al mover no: si la ruta
        // esta mal escrita, crearla en silencio deja una biblioteca vacia y el contenido
        // real huerfano, que es justo el accidente que hay que evitar.
        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException("La carpeta destino no existe.");
        }

        return resolved;
    }

    /// <summary>
    /// Reescribe el prefijo de las rutas absolutas guardadas al mover una biblioteca.
    /// <para>
    /// Obligatorio, no una optimizacion: el escaner empareja base de datos y disco por ruta
    /// completa, asi que sin esto cada serie pasaria a Missing y volveria a entrar con otro
    /// Id. Como <c>ReadingSession</c> cuelga de <c>Media.Id</c>, eso borra el progreso de
    /// lectura de toda la biblioteca.
    /// </para>
    /// </summary>
    private async Task RepointLibraryPathsAsync(string libraryId, string oldPath, string newPath, CancellationToken ct)
    {
        // substr es 1-based. Para la fila cuya ruta es exactamente la raiz devuelve "",
        // que deja solo la raiz nueva.
        var cut = oldPath.Length + 1;

        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE Media SET Path = {0} || substr(Path, {1})
            WHERE substr(Path, 1, {2}) = {3}
              AND SeriesId IN (SELECT Id FROM Series WHERE LibraryId = {4})
            """,
            [newPath, cut, oldPath.Length, oldPath, libraryId], ct);

        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE Series SET Path = {0} || substr(Path, {1})
            WHERE substr(Path, 1, {2}) = {3} AND LibraryId = {4}
            """,
            [newPath, cut, oldPath.Length, oldPath, libraryId], ct);

        // La ruta es la clave primaria aqui, asi que reescribirla choca si el destino ya se
        // habia escaneado. Borrar solo cuesta un escaneo frio.
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ScannedDirectories WHERE substr(Path, 1, {0}) = {1}",
            [oldPath.Length, oldPath], ct);
    }

    /// <summary>
    /// Borra el registro de la biblioteca, nunca los ficheros del disco: DiarSpeicher indexa
    /// carpetas que no son suyas y que pueden estar compartidas con otros programas.
    /// </summary>
    public async Task<bool> DeleteLibraryAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default)
    {
        var library = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (library == null) return false;

        // La carpeta va a la papelera antes de soltar el registro: si el movimiento falla,
        // la biblioteca sigue en el indice y el usuario no se queda con ficheros huerfanos
        // y sin nada que los describa.
        if (deleteFiles)
        {
            EnsureTrashed(library.Path, "la carpeta");
            await PurgeIndexUnderPathAsync(user, library.Path, ct);
        }

        await PurgeLibraryContentAsync(library.Id, ct);

        // La cache de mtimes no cuelga de la biblioteca por clave ajena, asi que sus filas
        // sobreviven al borrado y ningun escaneo posterior las visita: quedan para siempre.
        var libraryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(library.Path));
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ScannedDirectories WHERE substr(Path, 1, {0}) = {1}",
            [libraryPath.Length, libraryPath], ct);

        // La clave ajena va de Libraries hacia LibraryConfigs, no al reves, asi que la cascada
        // borra la biblioteca cuando cae la configuracion y no al contrario. Sin esto, cada
        // biblioteca borrada deja su LibraryConfig suelto.
        var configId = library.ConfigId;

        _db.Libraries.Remove(library);
        await _db.SaveChangesAsync(ct);

        var config = await _db.LibraryConfigs.FirstOrDefaultAsync(c => c.Id == configId, ct);
        if (config != null)
        {
            _db.LibraryConfigs.Remove(config);
            await _db.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<bool> DeleteSeriesAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series == null) return false;

        var seriesPath = series.Path;

        if (deleteFiles)
        {
            EnsureTrashed(seriesPath, "la carpeta de la serie");

            // Por ruta, no por SeriesId. El escaner recorre en profundidad, asi que un tomo
            // dentro de esta carpeta puede colgar de otra serie: se fue del disco con la
            // carpeta y tiene que irse del indice con ella.
            await PurgeIndexUnderPathAsync(user, seriesPath, ct);
        }

        // La purga por ruta puede haberse llevado ya esta misma serie.
        var remaining = await _db.Series.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (remaining == null) return true;

        if (!string.IsNullOrWhiteSpace(remaining.ThumbnailPath))
        {
            TryDeleteFile(remaining.ThumbnailPath);
        }

        // Mismo motivo que en la biblioteca: SetNull dejaria los tomos huerfanos.
        var media = await _db.Media.Where(m => m.SeriesId == remaining.Id).ToListAsync(ct);
        foreach (var item in media)
        {
            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath)) TryDeleteFile(item.ThumbnailPath);
        }

        _db.Media.RemoveRange(media);
        _db.Series.Remove(remaining);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<StumpMissingReportDto> GetMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Where(s => s.LibraryId == libraryId && s.Status == FileStatus.Missing)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Path,
                Volumes = s.Media.Count,
                Sessions = s.Media.Sum(m => m.ReadingSessions.Count)
            })
            .ToListAsync(ct);

        var report = new StumpMissingReportDto
        {
            Series = series.Select(s => new StumpMissingSeriesDto
            {
                Id = s.Id,
                Name = s.Name,
                Path = s.Path,
                VolumeCount = s.Volumes,
                ReadingSessions = s.Sessions
            }).ToList()
        };

        // Tomos perdidos sueltos: la carpeta sigue ahi pero el fichero no. Se cuentan
        // aparte porque no arrastran la serie entera.
        var orphans = await _db.Media.ForUser(user)
            .Where(m => m.Series != null
                && m.Series.LibraryId == libraryId
                && m.Status == FileStatus.Missing
                && m.Series.Status != FileStatus.Missing)
            .Select(m => new { Sessions = m.ReadingSessions.Count })
            .ToListAsync(ct);

        report.OrphanVolumes = orphans.Count;
        report.TotalVolumes = report.Series.Sum(s => s.VolumeCount) + orphans.Count;
        report.TotalReadingSessions = report.Series.Sum(s => s.ReadingSessions) + orphans.Sum(o => o.Sessions);

        return report;
    }

    /// <summary>
    /// Quita del indice lo que ya no esta en disco. Nunca toca un fichero.
    /// <para>
    /// Se vuelve a mirar el disco en vez de fiarse del estado guardado: si el volumen se
    /// desmonto durante el escaneo y volvio despues, esas filas dicen Missing y es mentira.
    /// Purgarlas se llevaria por delante el progreso de lectura de una biblioteca intacta.
    /// </para>
    /// </summary>
    public async Task<int> PurgeMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Where(s => s.LibraryId == libraryId && s.Status == FileStatus.Missing)
            .ToListAsync(ct);

        var goneSeries = series.Where(s => !Directory.Exists(s.Path)).ToList();

        var media = await _db.Media.ForUser(user)
            .Where(m => m.Series != null && m.Series.LibraryId == libraryId && m.Status == FileStatus.Missing)
            .ToListAsync(ct);

        var goneMedia = media.Where(m => !File.Exists(m.Path)).ToList();

        // Los tomos de una serie perdida ya estan en goneMedia: el escaner propaga Missing
        // hacia abajo, y si falta la carpeta tampoco existen sus ficheros.
        foreach (var item in goneMedia)
        {
            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath)) TryDeleteFile(item.ThumbnailPath);
        }

        foreach (var s in goneSeries)
        {
            if (!string.IsNullOrWhiteSpace(s.ThumbnailPath)) TryDeleteFile(s.ThumbnailPath);
        }

        _db.Media.RemoveRange(goneMedia);
        _db.Series.RemoveRange(goneSeries);
        await _db.SaveChangesAsync(ct);

        var removed = goneSeries.Count + goneMedia.Count;
        _logger.LogInformation(
            "Purgadas {Count} entradas perdidas de la biblioteca {Library}", removed, libraryId);

        return removed;
    }

    /// <summary>
    /// Saca del indice lo que colgaba de una ruta que acaba de desaparecer del disco.
    /// <para>
    /// Mover una carpeta a la papelera no avisa a la base de datos: sus filas se quedan
    /// apuntando a una ruta muerta y el tomo sigue saliendo en "anadido reciente" con su
    /// miniatura intacta, porque esa vive en el almacenamiento y no en la carpeta movida.
    /// </para>
    /// </summary>
    public async Task<int> PurgeIndexUnderPathAsync(AuthUser user, string path, CancellationToken ct = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix = root + Path.DirectorySeparatorChar;

        var media = await _db.Media.ForUser(user)
            .Where(m => m.Path == root || m.Path.StartsWith(prefix))
            .ToListAsync(ct);

        var series = await _db.Series.ForUser(user)
            .Where(s => s.Path == root || s.Path.StartsWith(prefix))
            .ToListAsync(ct);

        foreach (var thumb in media.Select(m => m.ThumbnailPath).Concat(series.Select(s => s.ThumbnailPath)))
        {
            if (!string.IsNullOrWhiteSpace(thumb)) TryDeleteFile(thumb);
        }

        _db.Media.RemoveRange(media);
        _db.Series.RemoveRange(series);
        await _db.SaveChangesAsync(ct);

        var removed = media.Count + series.Count;
        if (removed > 0)
        {
            _logger.LogInformation("Sacadas {Count} entradas del indice bajo {Path}", removed, root);
        }

        return removed;
    }

    /// <summary>
    /// Manda algo a la papelera, o explica por que no pudo. Que ya no este en disco no es un
    /// error: es justo cuando mas falta hace poder quitarlo del indice.
    /// </summary>
    private void EnsureTrashed(string path, string what)
    {
        switch (_trash.TryMoveToTrash(path, out _))
        {
            case TrashOutcome.Moved:
            case TrashOutcome.NotFound:
                return;
            case TrashOutcome.NotAllowed:
                throw new InvalidOperationException(
                    $"No se puede borrar {what} del disco: esa ruta esta fuera de las carpetas declaradas en el compose. El indice no se ha tocado.");
            default:
                throw new InvalidOperationException(
                    $"No se pudo mover {what} a la papelera. El indice no se ha tocado.");
        }
    }

    public async Task<bool> DeleteMediaAsync(AuthUser user, string id, bool deleteFile = false, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media == null) return false;

        if (deleteFile)
        {
            EnsureTrashed(media.Path, "el fichero");
            await PurgeIndexUnderPathAsync(user, media.Path, ct);

            // La purga por ruta ya se ha llevado esta fila y su miniatura.
            if (await _db.Media.FirstOrDefaultAsync(m => m.Id == id, ct) == null) return true;
        }

        if (!string.IsNullOrWhiteSpace(media.ThumbnailPath))
        {
            TryDeleteFile(media.ThumbnailPath);
        }

        _db.Media.Remove(media);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Borra las series y los tomos de una biblioteca, con sus miniaturas.
    /// <para>
    /// Hay que borrar los tomos a mano: la relacion Series-Media esta declarada
    /// <c>DeleteBehavior.SetNull</c>, asi que al irse la serie los tomos sobreviven con
    /// <c>SeriesId</c> nulo. Quedan huerfanos, invisibles desde la biblioteca y bien
    /// visibles en "anadido reciente".
    /// </para>
    /// </summary>
    private async Task PurgeLibraryContentAsync(string libraryId, CancellationToken ct)
    {
        var series = await _db.Series
            .Where(s => s.LibraryId == libraryId)
            .ToListAsync(ct);

        var media = await _db.Media
            .Where(m => m.Series != null && m.Series.LibraryId == libraryId)
            .ToListAsync(ct);

        foreach (var thumb in media.Select(m => m.ThumbnailPath).Concat(series.Select(s => s.ThumbnailPath)))
        {
            if (!string.IsNullOrWhiteSpace(thumb)) TryDeleteFile(thumb);
        }

        _db.Media.RemoveRange(media);
        _db.Series.RemoveRange(series);

        _logger.LogInformation(
            "Purgadas {Series} series y {Media} tomos de la biblioteca {Library}",
            series.Count, media.Count, libraryId);
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
            Description = s.Description ?? s.Metadata?.Summary,
            CoverUpdatedAt = s.ThumbnailMeta,
            Metadata = ToSeriesMetadataDto(s.Metadata)
        };
    }

    private static StumpSeriesMetadataDto? ToSeriesMetadataDto(SeriesMetadata? metadata)
    {
        if (metadata is null) return null;

        return new StumpSeriesMetadataDto
        {
            Source = metadata.MetaType,
            ExternalId = metadata.Comicid,
            Title = metadata.Title,
            Summary = metadata.Summary,
            Publisher = metadata.Publisher,
            Writers = metadata.Writers,
            Genres = metadata.Genres,
            Status = metadata.Status,
            Year = metadata.Year,
            CoverUrl = metadata.ComicImage,
            Link = metadata.Links,
            FinalVolume = metadata.TotalIssues,
            Type = metadata.Booktype,
            TotalChapters = metadata.PublicationRun
        };
    }
}

public sealed class UploadTooLargeException : Exception;
