using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Komga;

public class KomgaService : IKomgaService
{
    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<KomgaService> _logger;

    public KomgaService(DiarSpeicherDbContext db, ILogger<KomgaService> logger)
    {
        _db = db;
        _logger = logger;
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

        var sessions = await GetReadingSessionsForUserAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
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

        var sessions = await GetReadingSessionsForUserAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
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

    private async Task<Dictionary<string, ReadingSession>> GetReadingSessionsForUserAsync(
        string? userId,
        List<string> mediaIds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || mediaIds.Count == 0)
        {
            return new Dictionary<string, ReadingSession>();
        }

        var sessions = await _db.ReadingSessions
            .Where(s => s.UserId == userId && mediaIds.Contains(s.MediaId))
            .ToListAsync(ct);

        return sessions.ToDictionary(s => s.MediaId);
    }
}
