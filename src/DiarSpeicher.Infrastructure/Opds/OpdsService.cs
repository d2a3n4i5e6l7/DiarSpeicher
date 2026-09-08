using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public class OpdsService : IOpdsService
{
    private const int PageSize = 20;
    private const string CatalogEndpoint = "catalog";
    private const string SearchLiteral = "search";
    private readonly DiarSpeicherDbContext _db;
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly ILogger<OpdsService> _logger;

    public OpdsService(
        DiarSpeicherDbContext db,
        ICompositeBookProcessor bookProcessor,
        ILogger<OpdsService> logger)
    {
        _db = db;
        _bookProcessor = bookProcessor;
        _logger = logger;
    }

    private static string FormatUrl(string path, string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey)
            ? $"/opds/{apiKey}/v1.2/{path}"
            : $"/opds/v1.2/{path}";
    }

    private static string FormatParams(string path, Dictionary<string, string> queryParams, string? apiKey)
    {
        var url = FormatUrl(path, apiKey);
        if (queryParams.Count == 0) return url;

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{url}?{queryString}";
    }

    public Task<string> GetCatalogXmlAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        var entries = new List<OpdsEntry>
        {
            new()
            {
                Id = "keepReading",
                Title = "Keep reading",
                Summary = "Continue reading your in progress books",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("keep-reading", apiKey))]
            },
            new()
            {
                Id = "allSeries",
                Title = "All series",
                Summary = "Browse by series",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("series", apiKey))]
            },
            new()
            {
                Id = "latestSeries",
                Title = "Latest series",
                Summary = "Browse latest series",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("series/latest", apiKey))]
            },
            new()
            {
                Id = "allLibraries",
                Title = "All libraries",
                Summary = "Browse by library",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("libraries", apiKey))]
            },
            new()
            {
                Id = "allBooks",
                Title = "All books",
                Summary = "Browse all books",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("books", apiKey))]
            },
            new()
            {
                Id = "latestBooks",
                Title = "Latest books",
                Summary = "Browse latest books",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("books/latest", apiKey))]
            }
        };

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatUrl(CatalogEndpoint, apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey)),
            new(OpdsLinkType.Search, OpdsLinkRel.Search, FormatUrl(SearchLiteral, apiKey))
        };

        var feed = new OpdsFeed("root", "DiarSpeicher OPDS Catalog", links, entries);
        return Task.FromResult(OpdsXmlBuilder.BuildFeedXml(feed));
    }

    public string GetOpenSearchXml(string? apiKey)
    {
        var searchUrl = FormatUrl("search/feed?search={searchTerms}", apiKey);
        return OpdsXmlBuilder.BuildOpenSearchXml(searchUrl);
    }

    public async Task<string> GetLibrariesFeedAsync(AuthUser user, string? search, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Libraries.ForUser(user);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(l => l.Name.Contains(search));
        }

        var libraries = await query.OrderBy(l => l.Name).ToListAsync(ct);
        var entries = libraries.Select(l => ToOpdsEntry(l, apiKey)).ToList();

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatUrl("libraries", apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey))
        };

        var feed = new OpdsFeed("allLibraries", "All libraries", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetLibrarySeriesFeedAsync(AuthUser user, string libraryId, int page, string? apiKey, CancellationToken ct = default)
    {
        var library = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == libraryId, ct);

        if (library == null)
        {
            throw new KeyNotFoundException($"Library {libraryId} not found");
        }

        var query = _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Where(s => s.LibraryId == libraryId);

        var totalCount = await query.CountAsync(ct);
        var seriesList = await query
            .OrderBy(s => s.Name)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var entries = seriesList.Select(s => ToOpdsEntry(s, apiKey)).ToList();
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            library.Id,
            library.Name,
            entries,
            $"libraries/{library.Id}",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetSeriesFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Series.ForUser(user).Include(s => s.Metadata).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Name.Contains(search) || (s.Metadata != null && s.Metadata.Title != null && s.Metadata.Title.Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);
        var seriesList = await query
            .OrderBy(s => s.Name)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var entries = seriesList.Select(s => ToOpdsEntry(s, apiKey)).ToList();
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "allSeries",
            "All Series",
            entries,
            "series",
            page,
            totalCount,
            search,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetLatestSeriesFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Series.ForUser(user).Include(s => s.Metadata).AsQueryable();

        var totalCount = await query.CountAsync(ct);
        var seriesList = await query
            .OrderByDescending(s => s.Id)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var entries = seriesList.Select(s => ToOpdsEntry(s, apiKey)).ToList();
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "latestSeries",
            "Latest Series",
            entries,
            "series/latest",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetSeriesBooksFeedAsync(AuthUser user, string seriesId, int page, string? apiKey, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct);

        if (series == null)
        {
            throw new KeyNotFoundException($"Series {seriesId} not found");
        }

        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m => m.SeriesId == seriesId);

        var totalCount = await query.CountAsync(ct);
        var books = await query
            .OrderBy(m => m.Name)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var sessions = await GetReadingSessionsForUserAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var entries = books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey)).ToList();

        var title = series.Metadata?.Title ?? series.Name;
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            series.Id,
            title,
            entries,
            $"series/{series.Id}",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetBooksFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Media.ForUser(user).Include(m => m.Metadata).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m =>
                m.Name.Contains(search) ||
                (m.Metadata != null && m.Metadata.Title != null && m.Metadata.Title.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Summary != null && m.Metadata.Summary.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Writers != null && m.Metadata.Writers.Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);
        var books = await query
            .OrderBy(m => m.Name)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var sessions = await GetReadingSessionsForUserAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var entries = books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey)).ToList();

        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "allBooks",
            "All Books",
            entries,
            "books",
            page,
            totalCount,
            search,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetLatestBooksFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Media.ForUser(user).Include(m => m.Metadata).AsQueryable();

        var totalCount = await query.CountAsync(ct);
        var books = await query
            .OrderByDescending(m => m.Id)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var sessions = await GetReadingSessionsForUserAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var entries = books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey)).ToList();

        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "latestBooks",
            "Latest Books",
            entries,
            "books/latest",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetKeepReadingFeedXmlAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        var rawSessions = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.Status == ReadingStatus.Reading)
            .ToListAsync(ct);

        var sessions = rawSessions
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .ToList();
        var mediaIds = sessions.Select(s => s.MediaId).Distinct().ToList();

        var books = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m => mediaIds.Contains(m.Id))
            .ToListAsync(ct);

        var booksMap = books.ToDictionary(b => b.Id);
        var entries = new List<OpdsEntry>();

        foreach (var s in sessions)
        {
            if (booksMap.TryGetValue(s.MediaId, out var media))
            {
                entries.Add(ToOpdsEntry(media, s, apiKey));
            }
        }

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatUrl("keep-reading", apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey))
        };

        var feed = new OpdsFeed("keepReading", "Keep Reading", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetSearchFeedXmlAsync(AuthUser user, string? query, string? apiKey, CancellationToken ct = default)
    {
        var search = query?.Trim() ?? string.Empty;
        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatParams("search/feed", new Dictionary<string, string> { { SearchLiteral, search } }, apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey))
        };

        if (string.IsNullOrWhiteSpace(search))
        {
            var emptyFeed = new OpdsFeed("searchFeed", "Search Results", links, []);
            return OpdsXmlBuilder.BuildFeedXml(emptyFeed);
        }

        var entries = new List<OpdsEntry>();

        // 1. Libraries
        var libraries = await _db.Libraries.ForUser(user)
            .Where(l => l.Name.Contains(search))
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
        entries.AddRange(libraries.Select(l => ToOpdsEntry(l, apiKey)));

        // 2. Series
        var seriesList = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Where(s => s.Name.Contains(search) || (s.Metadata != null && s.Metadata.Title != null && s.Metadata.Title.Contains(search)))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        entries.AddRange(seriesList.Select(s => ToOpdsEntry(s, apiKey)));

        // 3. Books
        var books = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m =>
                m.Name.Contains(search) ||
                (m.Metadata != null && m.Metadata.Title != null && m.Metadata.Title.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Summary != null && m.Metadata.Summary.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Writers != null && m.Metadata.Writers.Contains(search)))
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        var sessions = await GetReadingSessionsForUserAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        entries.AddRange(books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey)));

        var feed = new OpdsFeed("searchFeed", "Search Results", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<(ExtractedPage? Page, Media? Media)> GetBookPageAsync(
        AuthUser user,
        string bookId,
        int pageNumber,
        bool zeroBased,
        bool trackProgression = true,
        CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == bookId, ct);

        if (book == null) return (null, null);

        var correctPage = zeroBased ? pageNumber + 1 : pageNumber;
        if (correctPage < 1 || (book.Pages > 0 && correctPage > book.Pages))
        {
            return (null, book);
        }

        if (trackProgression && !string.IsNullOrWhiteSpace(user.Id))
        {
            await RecordReadingProgressAsync(user, book, correctPage, ct);
        }

        var page = await _bookProcessor.ExtractPageAsync(book.Path, correctPage, ct);
        return (page, book);
    }

    private async Task RecordReadingProgressAsync(AuthUser user, Media book, int correctPage, CancellationToken ct)
    {
        try
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == user.Id, ct);
            if (!userExists) return;

            var percentage = book.Pages > 0 ? (decimal)correctPage / book.Pages : 0m;
            var session = await _db.ReadingSessions
                .FirstOrDefaultAsync(s => s.MediaId == book.Id && s.UserId == user.Id, ct);

            if (session == null)
            {
                session = new ReadingSession
                {
                    MediaId = book.Id,
                    UserId = user.Id,
                    StartPage = correctPage,
                    EndPage = correctPage,
                    StartPercentage = percentage,
                    EndPercentage = percentage,
                    Status = (book.Pages > 0 && correctPage >= book.Pages) ? ReadingStatus.Finished : ReadingStatus.Reading,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _db.ReadingSessions.Add(session);
            }
            else
            {
                session.EndPage = correctPage;
                session.EndPercentage = percentage;
                if (book.Pages > 0 && correctPage >= book.Pages)
                {
                    session.Status = ReadingStatus.Finished;
                }
                session.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record reading progression for user {UserId}, book {BookId}", user.Id, book.Id);
        }
    }

    public async Task<Media?> GetMediaForDownloadAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        return await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == bookId, ct);
    }

    public async Task<(byte[]? Data, string ContentType)> GetBookThumbnailAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == bookId, ct);
        if (book == null) return (null, "image/jpeg");

        if (!string.IsNullOrWhiteSpace(book.ThumbnailPath) && File.Exists(book.ThumbnailPath))
        {
            var bytes = await File.ReadAllBytesAsync(book.ThumbnailPath, ct);
            var ext = Path.GetExtension(book.ThumbnailPath);
            var mime = Core.Filesystem.ContentTypeExtensions.FromExtension(ext).MimeType();
            return (bytes, mime);
        }

        var page = await _bookProcessor.ExtractPageAsync(book.Path, 1, ct);
        if (page != null)
        {
            return (page.Data, page.ContentType.MimeType());
        }

        return (null, "image/jpeg");
    }

    private static OpdsEntry ToOpdsEntry(Library library, string? apiKey)
    {
        return new OpdsEntry
        {
            Id = library.Id,
            Title = library.Name,
            Updated = library.UpdatedAt ?? library.CreatedAt,
            Content = library.Description,
            Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl($"libraries/{library.Id}", apiKey))]
        };
    }

    private static OpdsEntry ToOpdsEntry(Series series, string? apiKey)
    {
        var title = series.Metadata?.Title ?? series.Name;
        var content = series.Metadata?.Summary ?? series.Description;

        return new OpdsEntry
        {
            Id = series.Id,
            Title = title,
            Updated = series.UpdatedAt ?? series.CreatedAt,
            Content = content,
            Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl($"series/{series.Id}", apiKey))]
        };
    }

    private static OpdsEntry ToOpdsEntry(Media media, ReadingSession? session, string? apiKey)
    {
        var title = media.Metadata?.Title ?? media.Name;
        var summary = media.Metadata?.Summary;
        var mib = media.Size / (1024.0 * 1024.0);
        var content = !string.IsNullOrWhiteSpace(summary)
            ? $"{mib:F1} MiB - {media.Extension}<br/><br/>{summary}"
            : $"{mib:F1} MiB - {media.Extension}";

        List<string>? authors = null;
        if (!string.IsNullOrWhiteSpace(media.Metadata?.Writers))
        {
            authors = media.Metadata.Writers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var fileName = Uri.EscapeDataString(Path.GetFileName(media.Path));
        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Thumbnail, FormatUrl($"books/{media.Id}/thumbnail", apiKey)),
            new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Image, FormatUrl($"books/{media.Id}/pages/0?zero_based=true", apiKey)),
            new(OpdsLinkType.FromExtension(media.Extension), OpdsLinkRel.Acquisition, FormatUrl($"books/{media.Id}/file/{fileName}", apiKey))
        };

        var streamLink = new OpdsStreamLink(
            media.Id,
            media.Pages,
            OpdsLinkType.ImageJpeg,
            session?.EndPage,
            session?.UpdatedAt?.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
            FormatUrl($"books/{media.Id}/pages/{{pageNumber}}?zero_based=true", apiKey)
        );

        return new OpdsEntry
        {
            Id = media.Id,
            Title = title,
            Updated = media.UpdatedAt ?? media.CreatedAt,
            Summary = summary,
            Content = content,
            Authors = authors,
            Links = links,
            StreamLink = streamLink
        };
    }

    private static OpdsFeed CreatePaginatedFeed(PaginatedFeedParams p)
    {
        var thisParams = new Dictionary<string, string> { { "page", p.Page.ToString() } };
        if (!string.IsNullOrWhiteSpace(p.Search)) thisParams[SearchLiteral] = p.Search;

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatParams(p.HrefPostfix, thisParams, p.ApiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, p.ApiKey))
        };

        if (p.Page > 0)
        {
            var prevParams = new Dictionary<string, string> { { "page", (p.Page - 1).ToString() } };
            if (!string.IsNullOrWhiteSpace(p.Search)) prevParams[SearchLiteral] = p.Search;
            links.Add(new(OpdsLinkType.Navigation, OpdsLinkRel.Previous, FormatParams(p.HrefPostfix, prevParams, p.ApiKey)));
        }

        var totalPages = (int)Math.Ceiling(p.TotalCount / (double)PageSize);
        if (p.Page < totalPages - 1 && p.Entries.Count == PageSize)
        {
            var nextParams = new Dictionary<string, string> { { "page", (p.Page + 1).ToString() } };
            if (!string.IsNullOrWhiteSpace(p.Search)) nextParams[SearchLiteral] = p.Search;
            links.Add(new(OpdsLinkType.Navigation, OpdsLinkRel.Next, FormatParams(p.HrefPostfix, nextParams, p.ApiKey)));
        }

        return new OpdsFeed(p.Id, p.Title, links, p.Entries);
    }

    private async Task<Dictionary<string, ReadingSession>> GetReadingSessionsForUserAsync(string? userId, List<string> mediaIds, CancellationToken ct)
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

    private sealed record PaginatedFeedParams(
        string Id,
        string Title,
        List<OpdsEntry> Entries,
        string HrefPostfix,
        int Page,
        int TotalCount,
        string? Search,
        string? ApiKey);
}
