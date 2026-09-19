using DiarSpeicher.Core.Domain.Komga;

namespace DiarSpeicher.Infrastructure.Komga;

public class KomgaService : IKomgaService
{
    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<KomgaService> _logger;

    private readonly ICompositeBookProcessor? _pageProcessor;
    private readonly IEpubProfileProvider? _epubProfileProvider;

    private readonly IReadingProgress _progress;

    public KomgaService(
        DiarSpeicherDbContext db,
        ILogger<KomgaService> logger,
        IReadingProgress progress,
        ICompositeBookProcessor? pageProcessor = null,
        IEpubProfileProvider? epubProfileProvider = null)
    {
        _db = db;
        _logger = logger;
        _pageProcessor = pageProcessor;
        _epubProfileProvider = epubProfileProvider;
        _progress = progress;
    }

    public async Task<List<KomgaLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default)
    {
        _logger.LogTrace("Fetching Komga libraries for user {UserId}", user.Id);
        var libraries = await _db.Libraries.ForUser(user)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        return libraries.Select(KomgaDtoMapper.ToLibraryDto).ToList();
    }

    public async Task<KomgaLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var lib = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        return lib == null ? null : KomgaDtoMapper.ToLibraryDto(lib);
    }

    public async Task<KomgaPageResponse<KomgaSeriesDto>> GetSeriesAsync(
        AuthUser user,
        string? libraryId,
        string? search,
        int page,
        int size,
        CancellationToken ct = default)
    {
        var query = _db.Series.ForUser(user).WithDetails().AsQueryable();

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

        var seriesDtos = seriesList.Select(KomgaDtoMapper.ToSeriesDto).ToList();
        return KomgaPageResponse<KomgaSeriesDto>.Create(seriesDtos, page, size, totalElements);
    }

    public async Task<KomgaSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .WithDetails()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return series == null ? null : KomgaDtoMapper.ToSeriesDto(series);
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

        query = query.MatchingText(search);

        var totalElements = await query.CountAsync(ct);
        var books = await query
            .OrderBy(m => m.SortName).ThenBy(m => m.Name)
            .Skip(page * size)
            .Take(size)
            .ToListAsync(ct);

        var sessions = await _db.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetLatestBooksAsync(
        AuthUser user,
        int page,
        int size,
        CancellationToken ct = default)
    {
        var (where, parameters) = MediaSqlFilters.BuildVisibilityFilter(user);

        var countSql = MediaSqlFilters.BuildCountSql(where);
        var pageSql = MediaSqlFilters.BuildLatestPageSql(where);

        var totalElements = await _db.Database
            .SqlQueryRaw<int>(countSql, parameters.ToParameters())
            .SingleAsync(ct);

        var books = await _db.Media
            .FromSqlRaw(pageSql, parameters.ToParameters(("@take", size), ("@skip", page * size)))
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .AsNoTracking()
            .ToListAsync(ct);

        var sessions = await _db.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

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
            .OrderBy(m => m.SortName).ThenBy(m => m.Name)
            .Skip(page * size)
            .Take(size)
            .ToListAsync(ct);

        var sessions = await _db.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

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

        var progress = await _progress.ResolveAsync(book, session, ct);

        return KomgaDtoMapper.ToBookDto(book, session, progress.TotalPages, overrideCurrentPage: progress.Page);
    }

    public async Task<List<KomgaBookPageDto>> GetBookPagesAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (book == null || book.Pages <= 0) return [];

        if (EpubPageMapStore.IsEpub(book))
        {
            return await GetEpubBookPagesAsync(book, ct);
        }

        var measured = await _db.MediaPages
            .Where(p => p.MediaId == book.Id)
            .OrderBy(p => p.Number)
            .ToListAsync(ct);

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

        var fallbackPages = new List<KomgaBookPageDto>();
        for (var i = 1; i <= book.Pages; i++)
        {
            fallbackPages.Add(new KomgaBookPageDto
            {
                Number = i,
                FileName = $"page_{i:D4}.jpg",
                MediaType = "image/jpeg"
            });
        }
        return fallbackPages;
    }

    private async Task<List<MediaPage>> MeasurePagesAsync(Media book, CancellationToken ct)
    {
        if (_pageProcessor is null) return [];
        if (EpubPageMapStore.IsEpub(book)) return [];

        _logger.LogInformation(
            "Midiendo {Pages} paginas de {BookId}; la primera apertura de un libro es lenta",
            book.Pages, book.Id);

        var analyzed = await _pageProcessor.AnalyzeAsync(book.Path, new BookAnalysisOptions { MeasurePages = true }, ct);

        var rows = analyzed.PageDimensions.Select(p => new MediaPage
        {
            MediaId = book.Id,
            Number = p.Number,
            FileName = p.FileName,
            MediaType = p.MediaType,
            Width = p.Width,
            Height = p.Height,
            SizeBytes = p.SizeBytes
        }).ToList();

        try
        {
            _db.MediaPages.AddRange(rows);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
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
        if (book == null) return false;

        var ok = await _progress.RecordAsync(user, book, new ReadingProgressUpdate(page, completed), ct);
        if (ok)
        {
            _logger.LogTrace("Updated Komga read progress for user {UserId}, book {BookId}, page {Page}", user.Id, bookId, page);
        }
        return ok;
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


    private async Task CleanStalePagesAsync(string mediaId, CancellationToken ct)
    {
        var stalePages = await _db.MediaPages
            .Where(p => p.MediaId == mediaId)
            .ToListAsync(ct);
        if (stalePages.Count > 0)
        {
            _db.MediaPages.RemoveRange(stalePages);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<List<KomgaBookPageDto>> GetEpubBookPagesAsync(Media book, CancellationToken ct)
    {
        await CleanStalePagesAsync(book.Id, ct);

        var profile = _epubProfileProvider != null
            ? await _epubProfileProvider.GetCurrentProfileAsync(ct)
            : EpubDeviceProfile.GetDefaults()[0];

        await using var archive = await ZipFile.OpenReadAsync(book.Path, ct);
        var spine = await EpubBookProcessor.GetSpineEntriesAsync(archive, ct);
        if (spine.Count == 0) spine = EpubBookProcessor.GetFallbackHtmlEntries(archive);
        var cover = await EpubBookProcessor.FindCoverEntryAsync(archive, ct);
        var map = await EpubRasterizer.GetOrBuildPageMapAsync(archive, book.Path, spine, cover, profile, ct);

        var pages = new List<KomgaBookPageDto>(map.TotalPages);
        for (var i = 1; i <= map.TotalPages; i++)
        {
            pages.Add(BuildBookPageDto(map.Pages[i - 1], i, profile));
        }
        return pages;
    }

    private static KomgaBookPageDto BuildBookPageDto(EpubSubpageTarget target, int pageNumber, EpubDeviceProfile profile)
    {
        int width = profile.Width;
        int? height = profile.Height;
        string mediaType = "image/webp";

        if (target.IsImageOnly && target.ImageWidth.HasValue && target.ImageHeight.HasValue)
        {
            width = target.ImageWidth.Value;
            height = target.ImageHeight.Value;
            var ext = Path.GetExtension(target.EntryFullName).ToLowerInvariant();
            mediaType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        return new KomgaBookPageDto
        {
            Number = pageNumber,
            FileName = $"page_{pageNumber:D4}.webp",
            MediaType = mediaType,
            Width = width,
            Height = height
        };
    }
}

