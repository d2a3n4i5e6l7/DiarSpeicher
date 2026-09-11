using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using SkiaSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Komga;

public class KomgaService : IKomgaService
{
    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<KomgaService> _logger;

    private readonly ICompositeBookProcessor? _pageProcessor;

    /// <summary>
    /// El procesador es opcional: sin el, la lista de paginas sale sin dimensiones, que es
    /// como se comportaba antes. Asi los tests que construyen el servicio a mano siguen
    /// valiendo y solo produccion paga el coste de medir.
    /// </summary>
    public KomgaService(
        DiarSpeicherDbContext db,
        ILogger<KomgaService> logger,
        ICompositeBookProcessor? pageProcessor = null)
    {
        _db = db;
        _logger = logger;
        _pageProcessor = pageProcessor;
    }

    public async Task<List<KomgaLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default)
    {
        _logger.LogTrace("Fetching Komga libraries for user {UserId}", user.Id);
        var libraries = await _db.Libraries.ForUser(user)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        return libraries.Select(ToLibraryDto).ToList();
    }

    public async Task<KomgaLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var lib = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        return lib == null ? null : ToLibraryDto(lib);
    }

    public async Task<KomgaPageResponse<KomgaSeriesDto>> GetSeriesAsync(
        AuthUser user,
        string? libraryId,
        string? search,
        int page,
        int size,
        CancellationToken ct = default)
    {
        var query = _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Include(s => s.Media)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            query = query.Where(s => s.LibraryId == libraryId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Name.Contains(search) || (s.Metadata != null && s.Metadata.Title != null && s.Metadata.Title.Contains(search)));
        }

        var totalElements = await query.CountAsync(ct);
        var seriesList = await query
            .OrderBy(s => s.Name)
            .Skip(page * size)
            .Take(size)
            .ToListAsync(ct);

        var seriesDtos = seriesList.Select(ToSeriesDto).ToList();
        return KomgaPageResponse<KomgaSeriesDto>.Create(seriesDtos, page, size, totalElements);
    }

    public async Task<KomgaSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Include(s => s.Media)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return series == null ? null : ToSeriesDto(series);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetBooksAsync(
        AuthUser user,
        string? search,
        int page,
        int size,
        CancellationToken ct = default)
    {
        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m =>
                m.Name.Contains(search) ||
                (m.Metadata != null && m.Metadata.Title != null && m.Metadata.Title.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Summary != null && m.Metadata.Summary.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Writers != null && m.Metadata.Writers.Contains(search)));
        }

        var totalElements = await query.CountAsync(ct);
        var books = await query
            .OrderBy(m => m.Name)
            .Skip(page * size)
            .Take(size)
            .ToListAsync(ct);

        var sessions = await _db.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var bookDtos = books.Select(b => ToBookDto(b, sessions.GetValueOrDefault(b.Id))).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetLatestBooksAsync(
        AuthUser user,
        int page,
        int size,
        CancellationToken ct = default)
    {
        // Written as raw SQL because EF cannot translate ORDER BY over a DateTimeOffset on
        // SQLite; the visibility filter mirrors ForUser exactly.
        var (where, parameters) = MediaSqlFilters.BuildVisibilityFilter(user);

        // EF1002 warns that an interpolated string reaches the SQL unparameterised. The only
        // interpolated values here are MediaSqlFilters.Joins, a const, and the clause text from
        // BuildVisibilityFilter, which emits nothing but literal SQL and "@name" placeholders:
        // every value travels through the parameter list. Switching to FromSql would try to
        // parameterise the fragments themselves, which is not what they are.
#pragma warning disable EF1002
        var totalElements = await _db.Database
            .SqlQueryRaw<int>(
                $"""
                 SELECT COUNT(*) AS "Value"
                 {MediaSqlFilters.Joins}
                 WHERE {where}
                 """,
                parameters.ToParameters())
            .SingleAsync(ct);

        var books = await _db.Media
            .FromSqlRaw(
                $"""
                 SELECT m.*
                 {MediaSqlFilters.Joins}
                 WHERE {where}
                 ORDER BY m."CreatedAt" DESC, m."Id" DESC
                 LIMIT @take OFFSET @skip
                 """,
                parameters.ToParameters(("@take", size), ("@skip", page * size)))
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .AsNoTracking()
            .ToListAsync(ct);
#pragma warning restore EF1002

        var sessions = await _db.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var bookDtos = books.Select(b => ToBookDto(b, sessions.GetValueOrDefault(b.Id))).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetBooksInSeriesAsync(
        AuthUser user,
        string seriesId,
        int page,
        int size,
        CancellationToken ct = default)
    {
        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .Where(m => m.SeriesId == seriesId);

        var totalElements = await query.CountAsync(ct);
        var books = await query
            .OrderBy(m => m.Name)
            .Skip(page * size)
            .Take(size)
            .ToListAsync(ct);

        var sessions = await _db.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var bookDtos = books.Select(b => ToBookDto(b, sessions.GetValueOrDefault(b.Id))).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaBookDto?> GetBookByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (book == null) return null;

        var session = string.IsNullOrWhiteSpace(user.Id)
            ? null
            : await _db.ReadingSessions.FirstOrDefaultAsync(s => s.MediaId == id && s.UserId == user.Id, ct);

        return ToBookDto(book, session);
    }

    public async Task<List<KomgaBookPageDto>> GetBookPagesAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (book == null || book.Pages <= 0) return [];

        var measured = await _db.MediaPages
            .Where(p => p.MediaId == book.Id)
            .OrderBy(p => p.Number)
            .ToListAsync(ct);

        // Fallback para libros indexados antes de que el scan midiera las paginas. El scan
        // las persiste desde entonces, asi que esta rama solo cubre lo heredado y deja de
        // ejecutarse tras el primer rescan.
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

        // Sin procesador o con el archivo ilegible: la lista sintetizada de siempre. Los
        // clientes de Komga no abriran el libro, pero el resto de la API sigue respondiendo.
        var pages = new List<KomgaBookPageDto>();
        for (var i = 1; i <= book.Pages; i++)
        {
            pages.Add(new KomgaBookPageDto
            {
                Number = i,
                FileName = $"page_{i:D4}.jpg",
                MediaType = "image/jpeg"
            });
        }
        return pages;
    }

    /// <summary>
    /// Abre el libro una sola vez en su vida y guarda las dimensiones de cada pagina. Es
    /// caro —descomprime el archivo entero— y por eso el resultado se persiste: la segunda
    /// llamada y siguientes salen de la base.
    /// </summary>
    private async Task<List<MediaPage>> MeasurePagesAsync(Media book, CancellationToken ct)
    {
        if (_pageProcessor is null) return [];

        _logger.LogInformation(
            "Midiendo {Pages} paginas de {BookId}; la primera apertura de un libro es lenta",
            book.Pages, book.Id);

        var rows = new List<MediaPage>(book.Pages);
        for (var i = 1; i <= book.Pages; i++)
        {
            ct.ThrowIfCancellationRequested();

            var row = new MediaPage
            {
                MediaId = book.Id,
                Number = i,
                FileName = $"page_{i:D4}.jpg",
                MediaType = "image/jpeg"
            };

            try
            {
                var page = await _pageProcessor.ExtractPageAsync(book.Path, i, ct);
                if (page is not null && page.Data.Length > 0)
                {
                    row.MediaType = page.ContentType.ToMimeType();
                    row.SizeBytes = page.Data.Length;

                    // SKCodec lee solo la cabecera: no decodifica la imagen entera para
                    // averiguar cuanto mide.
                    using var data = SKData.CreateCopy(page.Data);
                    using var codec = SKCodec.Create(data);
                    if (codec is not null)
                    {
                        row.Width = codec.Info.Width;
                        row.Height = codec.Info.Height;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una pagina corrupta no puede tumbar el listado entero: se guarda sin
                // dimensiones y las demas siguen.
                _logger.LogWarning(ex, "No se pudo medir la pagina {Page} de {BookId}", i, book.Id);
            }

            rows.Add(row);
        }

        try
        {
            _db.MediaPages.AddRange(rows);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Dos peticiones simultaneas sobre el mismo libro miden a la vez y la segunda
            // choca con la clave compuesta. El trabajo ya esta hecho: se lee lo guardado.
            _logger.LogDebug(ex, "Otra peticion ya guardo las paginas de {BookId}", book.Id);
            foreach (var row in rows)
            {
                _db.Entry(row).State = EntityState.Detached;
            }

            return await _db.MediaPages
                .Where(p => p.MediaId == book.Id)
                .OrderBy(p => p.Number)
                .ToListAsync(ct);
        }

        return rows;
    }

    public async Task<bool> UpdateReadProgressAsync(
        AuthUser user,
        string bookId,
        int page,
        bool completed,
        CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == bookId, ct);
        if (book == null || string.IsNullOrWhiteSpace(user.Id)) return false;

        var userExists = await _db.Users.AnyAsync(u => u.Id == user.Id, ct);
        if (!userExists) return false;

        var session = await _db.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == bookId && s.UserId == user.Id, ct);

        var actualPage = Math.Min(page, book.Pages > 0 ? book.Pages : page);
        var percentage = book.Pages > 0 ? (decimal)actualPage / book.Pages : 0m;
        var isFinished = completed || (book.Pages > 0 && actualPage >= book.Pages);

        if (session == null)
        {
            session = new ReadingSession
            {
                MediaId = bookId,
                UserId = user.Id,
                StartPage = actualPage,
                EndPage = actualPage,
                StartPercentage = percentage,
                EndPercentage = percentage,
                Status = isFinished ? ReadingStatus.Finished : ReadingStatus.Reading,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.ReadingSessions.Add(session);
        }
        else
        {
            session.EndPage = actualPage;
            session.EndPercentage = percentage;
            session.Status = isFinished ? ReadingStatus.Finished : ReadingStatus.Reading;
            session.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogTrace("Updated Komga read progress for user {UserId}, book {BookId}, page {Page}", user.Id, bookId, page);
        return true;
    }

    public async Task<bool> DeleteReadProgressAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return false;

        var session = await _db.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == bookId && s.UserId == user.Id, ct);

        if (session == null) return false;

        _db.ReadingSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        _logger.LogTrace("Deleted Komga read progress for user {UserId}, book {BookId}", user.Id, bookId);
        return true;
    }

    private static KomgaLibraryDto ToLibraryDto(Library l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Root = l.Path
    };

    private static KomgaSeriesDto ToSeriesDto(Series s)
    {
        var title = s.Metadata?.Title ?? s.Name;
        var booksCount = s.Media.Count(m => m.DeletedAt == null);

        return new KomgaSeriesDto
        {
            Id = s.Id,
            LibraryId = s.LibraryId ?? string.Empty,
            Name = s.Name,
            Url = s.Path,
            Created = s.CreatedAt.ToString("O"),
            LastModified = (s.UpdatedAt ?? s.CreatedAt).ToString("O"),
            FileLastModified = (s.UpdatedAt ?? s.CreatedAt).ToString("O"),
            BooksCount = booksCount,
            BooksUnreadCount = booksCount,
            Metadata = new KomgaSeriesMetadataDto
            {
                Title = title,
                Summary = s.Metadata?.Summary ?? s.Description ?? string.Empty,
                AgeRating = s.Metadata?.AgeRating
            }
        };
    }

    private static KomgaBookDto ToBookDto(Media m, ReadingSession? s)
    {
        var title = m.Metadata?.Title ?? m.Name;
        var ext = ContentTypeExtensions.FromExtension(m.Extension);
        var mib = m.Size / (1024.0 * 1024.0);

        List<KomgaAuthorDto> authors = [];
        if (!string.IsNullOrWhiteSpace(m.Metadata?.Writers))
        {
            authors = m.Metadata.Writers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => new KomgaAuthorDto { Name = w, Role = "writer" })
                .ToList();
        }

        KomgaReadProgressDto? readProgress = null;
        if (s != null)
        {
            readProgress = new KomgaReadProgressDto
            {
                Page = s.EndPage ?? 1,
                Completed = s.Status == ReadingStatus.Finished,
                ReadDate = (s.UpdatedAt ?? s.CreatedAt).ToString("O"),
                Created = s.CreatedAt.ToString("O"),
                LastModified = (s.UpdatedAt ?? s.CreatedAt).ToString("O")
            };
        }

        return new KomgaBookDto
        {
            Id = m.Id,
            SeriesId = m.SeriesId ?? string.Empty,
            SeriesTitle = m.Series?.Name ?? string.Empty,
            LibraryId = m.Series?.LibraryId ?? string.Empty,
            Name = m.Name,
            Url = m.Path,
            Created = m.CreatedAt.ToString("O"),
            LastModified = (m.UpdatedAt ?? m.CreatedAt).ToString("O"),
            FileLastModified = (m.UpdatedAt ?? m.CreatedAt).ToString("O"),
            SizeBytes = m.Size,
            Size = $"{mib:F1} MB",
            Media = new KomgaMediaDto
            {
                Status = "READY",
                MediaType = ext.ToMimeType(),
                PagesCount = m.Pages
            },
            Metadata = new KomgaBookMetadataDto
            {
                Title = title,
                Summary = m.Metadata?.Summary ?? string.Empty,
                Authors = authors
            },
            ReadProgress = readProgress
        };
    }

}
