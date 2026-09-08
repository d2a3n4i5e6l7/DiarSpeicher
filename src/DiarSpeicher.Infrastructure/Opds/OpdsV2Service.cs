using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public class OpdsV2Service : IOpdsV2Service
{
    private const int PageSize = 20;
    private const string CatalogEndpoint = "catalog";
    private const string StartRel = "start";
    private const string TitleKey = "title";

    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<OpdsV2Service> _logger;

    public OpdsV2Service(DiarSpeicherDbContext db, ILogger<OpdsV2Service> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static string FormatUrl(string path, string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey)
            ? $"/opds/{apiKey}/v2.0/{path}"
            : $"/opds/v2.0/{path}";
    }

    public OpdsV2AuthenticationDoc GetAuthenticationDoc(string? apiKey)
    {
        return new OpdsV2AuthenticationDoc
        {
            Type = "http" + "://opds-spec.org/auth/basic",
            Title = "DiarSpeicher OPDS 2.0",
            Links =
            [
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ]
        };
    }

    public async Task<OpdsV2Feed> GetCatalogFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        _logger.LogTrace("Generating OPDS 2.0 root catalog for user {UserId}", user.Id);

        var feed = new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "DiarSpeicher OPDS 2.0 Catalog",
                Modified = DateTimeOffset.UtcNow.ToString("O")
            },
            Links =
            [
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel),
                new(FormatUrl("search{?query}", apiKey), OpdsV2MimeTypes.OpdsJson, "search") { Templated = true }
            ],
            Navigation =
            [
                new(FormatUrl("libraries", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "Libraries" } },
                new(FormatUrl("series", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "All Series" } },
                new(FormatUrl("books/browse", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "All Books" } },
                new(FormatUrl("books/latest", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "Latest Books" } },
                new(FormatUrl("books/keep-reading", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "Keep Reading" } }
            ]
        };

        var keepReadingFeed = await GetKeepReadingFeedAsync(user, apiKey, ct);
        if (keepReadingFeed.Publications.Count > 0)
        {
            feed.Groups.Add(new OpdsV2FeedGroup
            {
                Metadata = new OpdsV2Metadata { Title = "Keep Reading" },
                Links = [new(FormatUrl("books/keep-reading", apiKey), OpdsV2MimeTypes.OpdsJson, "self")],
                Publications = keepReadingFeed.Publications
            });
        }

        var latestBooksFeed = await GetBooksFeedAsync(user, 0, apiKey, ct);
        if (latestBooksFeed.Publications.Count > 0)
        {
            feed.Groups.Add(new OpdsV2FeedGroup
            {
                Metadata = new OpdsV2Metadata { Title = "Latest Books" },
                Links = [new(FormatUrl("books/latest", apiKey), OpdsV2MimeTypes.OpdsJson, "self")],
                Publications = latestBooksFeed.Publications.Take(10).ToList()
            });
        }

        return feed;
    }

    public async Task<OpdsV2Feed> SearchFeedAsync(AuthUser user, string query, string? apiKey, CancellationToken ct = default)
    {
        var search = query?.Trim() ?? string.Empty;
        var feed = new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = $"Search Results: {search}",
                Modified = DateTimeOffset.UtcNow.ToString("O")
            },
            Links =
            [
                new(FormatUrl($"search?query={Uri.EscapeDataString(search)}", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ]
        };

        if (string.IsNullOrWhiteSpace(search)) return feed;

        var books = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .Where(m =>
                m.Name.Contains(search) ||
                (m.Metadata != null && m.Metadata.Title != null && m.Metadata.Title.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Summary != null && m.Metadata.Summary.Contains(search)) ||
                (m.Metadata != null && m.Metadata.Writers != null && m.Metadata.Writers.Contains(search)))
            .OrderBy(m => m.Name)
            .Take(PageSize)
            .ToListAsync(ct);

        feed.Publications = books.Select(b => ToPublication(b, apiKey)).ToList();
        return feed;
    }

    public async Task<OpdsV2Feed> GetLibrariesFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        var libraries = await _db.Libraries.ForUser(user)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "Libraries",
                NumberOfItems = libraries.Count
            },
            Links =
            [
                new(FormatUrl("libraries", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ],
            Navigation = libraries.Select(l => new OpdsV2Link(FormatUrl($"libraries/{l.Id}/books", apiKey), OpdsV2MimeTypes.OpdsJson)
            {
                Properties = new() { [TitleKey] = l.Name }
            }).ToList()
        };
    }

    public async Task<OpdsV2Feed> GetSeriesFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Series.ForUser(user).Include(s => s.Metadata);
        var totalCount = await query.CountAsync(ct);

        var seriesList = await query
            .OrderBy(s => s.Name)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        var links = new List<OpdsV2Link>
        {
            new(FormatUrl($"series?page={page}", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
            new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
        };

        if (page > 0)
        {
            links.Add(new(FormatUrl($"series?page={page - 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "previous"));
        }
        if (page < totalPages - 1)
        {
            links.Add(new(FormatUrl($"series?page={page + 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "next"));
        }

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "All Series",
                NumberOfItems = totalCount,
                ItemsPerPage = PageSize,
                CurrentPage = page
            },
            Links = links,
            Navigation = seriesList.Select(s => new OpdsV2Link(FormatUrl($"series/{s.Id}", apiKey), OpdsV2MimeTypes.OpdsJson)
            {
                Properties = new() { [TitleKey] = s.Metadata?.Title ?? s.Name }
            }).ToList()
        };
    }

    public async Task<OpdsV2Feed> GetBooksFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
    {
        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series);

        var totalCount = await query.CountAsync(ct);
        var books = await query
            .OrderByDescending(m => m.Id)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        var links = new List<OpdsV2Link>
        {
            new(FormatUrl($"books/browse?page={page}", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
            new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
        };

        if (page > 0)
        {
            links.Add(new(FormatUrl($"books/browse?page={page - 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "previous"));
        }
        if (page < totalPages - 1)
        {
            links.Add(new(FormatUrl($"books/browse?page={page + 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "next"));
        }

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "Books",
                NumberOfItems = totalCount,
                ItemsPerPage = PageSize,
                CurrentPage = page
            },
            Links = links,
            Publications = books.Select(b => ToPublication(b, apiKey)).ToList()
        };
    }

    public async Task<OpdsV2Feed> GetKeepReadingFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
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
            .Include(m => m.Series)
            .Where(m => mediaIds.Contains(m.Id))
            .ToListAsync(ct);

        var booksMap = books.ToDictionary(b => b.Id);
        var publications = new List<OpdsV2Publication>();

        foreach (var s in sessions)
        {
            if (booksMap.TryGetValue(s.MediaId, out var media))
            {
                publications.Add(ToPublication(media, apiKey));
            }
        }

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "Keep Reading",
                NumberOfItems = publications.Count
            },
            Links =
            [
                new(FormatUrl("books/keep-reading", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ],
            Publications = publications
        };
    }

    public async Task<OpdsV2Publication?> GetPublicationAsync(AuthUser user, string bookId, string? apiKey, CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .FirstOrDefaultAsync(m => m.Id == bookId, ct);

        return book == null ? null : ToPublication(book, apiKey, includeReadingOrder: true);
    }

    public async Task<OpdsV2Progression?> GetProgressionAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        var session = await _db.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == bookId && s.UserId == user.Id, ct);

        if (session == null) return null;

        return new OpdsV2Progression
        {
            Page = session.EndPage,
            Percentage = session.EndPercentage,
            Modified = session.UpdatedAt?.ToString("O") ?? session.CreatedAt.ToString("O"),
            Device = "OPDS-2.0-Client"
        };
    }

    public async Task<bool> UpdateProgressionAsync(AuthUser user, string bookId, OpdsV2Progression progression, CancellationToken ct = default)
    {
        var book = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == bookId, ct);
        if (book == null || string.IsNullOrWhiteSpace(user.Id)) return false;

        var userExists = await _db.Users.AnyAsync(u => u.Id == user.Id, ct);
        if (!userExists) return false;

        var session = await _db.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == bookId && s.UserId == user.Id, ct);

        var page = progression.Page ?? 1;
        var percentage = progression.Percentage ?? (book.Pages > 0 ? (decimal)page / book.Pages : 0m);

        if (session == null)
        {
            session = new ReadingSession
            {
                MediaId = bookId,
                UserId = user.Id,
                StartPage = page,
                EndPage = page,
                StartPercentage = percentage,
                EndPercentage = percentage,
                Status = (book.Pages > 0 && page >= book.Pages) ? ReadingStatus.Finished : ReadingStatus.Reading,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.ReadingSessions.Add(session);
        }
        else
        {
            session.EndPage = page;
            session.EndPercentage = percentage;
            if (book.Pages > 0 && page >= book.Pages)
            {
                session.Status = ReadingStatus.Finished;
            }
            session.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogTrace("Updated OPDS 2.0 reading progression for user {UserId}, book {BookId}, page {Page}", user.Id, bookId, page);
        return true;
    }

    private static OpdsV2Publication ToPublication(Media media, string? apiKey, bool includeReadingOrder = false)
    {
        var title = media.Metadata?.Title ?? media.Name;
        var summary = media.Metadata?.Summary;
        var modified = (media.UpdatedAt ?? media.CreatedAt).ToString("O");
        var ext = ContentTypeExtensions.FromExtension(media.Extension);

        var publication = new OpdsV2Publication
        {
            Metadata = new OpdsV2Metadata
            {
                Title = title,
                Identifier = $"urn:uuid:{media.Id}",
                Type = "http" + "://schema.org/ComicStory",
                Modified = modified,
                Description = summary,
                NumberOfPages = media.Pages,
                ReadingProgression = "ltr"
            },
            Images =
            [
                new(FormatUrl($"books/{media.Id}/thumbnail", apiKey), "image/jpeg")
            ],
            Links =
            [
                new(FormatUrl($"books/{media.Id}", apiKey), OpdsV2MimeTypes.PublicationJson, "self"),
                new(FormatUrl($"books/{media.Id}/file", apiKey), ext.ToMimeType(), "http" + "://opds-spec.org/acquisition"),
                new(FormatUrl($"books/{media.Id}/progression", apiKey), "application/json", "http" + "://vaemendis.net/opds-pse/stream")
            ]
        };

        if (includeReadingOrder && media.Pages > 0)
        {
            for (var p = 0; p < media.Pages; p++)
            {
                publication.ReadingOrder.Add(new OpdsV2Link(
                    FormatUrl($"books/{media.Id}/pages/{p}?zero_based=true", apiKey),
                    "image/jpeg"
                ));
            }
        }

        return publication;
    }
}
