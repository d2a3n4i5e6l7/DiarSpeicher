using System.Text;
using System.Text.Json;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Sync;

public sealed class KoboService : IKoboService
{
    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<KoboService> _logger;

    public KoboService(
        DiarSpeicherDbContext db,
        ILogger<KoboService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<Dictionary<string, object>> GetInitializationAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        var cleanBaseUrl = baseUrl.TrimEnd('/');
        var imageTemplate = $"{cleanBaseUrl}/kobo/{apiKey}/v1/books/{{ImageId}}/thumbnail/{{Width}}/{{Height}}/{{IsGreyscale}}/image.jpg";
        var qualityTemplate = $"{cleanBaseUrl}/kobo/{apiKey}/v1/books/{{ImageId}}/thumbnail/{{Width}}/{{Height}}/{{Quality}}/{{IsGreyscale}}/image.jpg";

        var resources = new Dictionary<string, object>
        {
            ["image_host"] = cleanBaseUrl,
            ["image_url_template"] = imageTemplate,
            ["image_url_quality_template"] = qualityTemplate,
            ["account_page"] = "https://www.kobo.com/account/settings",
            ["assets"] = "https://storeapi.kobo.com/v1/assets",
            ["book_detail_page"] = "https://www.kobo.com/{region}/{language}/ebook/{slug}",
            ["categories"] = "https://storeapi.kobo.com/v1/categories",
            ["configuration_data"] = "https://storeapi.kobo.com/v1/configuration"
        };

        return Task.FromResult(resources);
    }

    public async Task<KoboSyncResponse> SyncLibraryAsync(
        AuthUser user,
        string baseUrl,
        string apiKey,
        string? clientSyncToken,
        int limit = 100,
        CancellationToken ct = default)
    {
        var offset = ParseSyncTokenOffset(clientSyncToken);

        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .Where(m => m.Extension == "epub" || m.Extension == ".epub")
            .OrderBy(m => m.Id);

        _logger.LogInformation("Kobo sync for user {UserId}, offset {Offset}", user.Id, offset);
        var mediaList = await query.Skip(offset).Take(limit + 1).ToListAsync(ct);

        var hasMore = mediaList.Count > limit;
        if (hasMore)
        {
            mediaList = mediaList.Take(limit).ToList();
        }

        var mediaIds = mediaList.Select(m => m.Id).ToList();
        var sessions = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && mediaIds.Contains(s.MediaId))
            .ToListAsync(ct);

        var sessionMap = sessions
            .GroupBy(s => s.MediaId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Id).First());

        var items = mediaList.Select(m => ToKoboEntitlementContainer(m, baseUrl, apiKey, sessionMap.GetValueOrDefault(m.Id))).ToList();

        var nextOffset = offset + mediaList.Count;
        var nextToken = hasMore
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"offset:{nextOffset}"))
            : Convert.ToBase64String(Encoding.UTF8.GetBytes($"offset:{nextOffset}:done"));

        return new KoboSyncResponse
        {
            Items = items,
            SyncToken = nextToken,
            ShouldContinue = hasMore
        };
    }

    public async Task<KoboBookMetadata?> GetBookMetadataAsync(
        AuthUser user,
        string baseUrl,
        string apiKey,
        string bookId,
        CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Include(m => m.Series)
            .FirstOrDefaultAsync(m => m.Id == bookId, ct);

        if (media == null)
        {
            return null;
        }

        var container = ToKoboEntitlementContainer(media, baseUrl, apiKey, null);
        return container.BookMetadata;
    }

    public async Task<(string Path, string ContentType)?> GetBookFileAsync(
        AuthUser user,
        string bookId,
        CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == bookId, ct);

        if (media == null || !File.Exists(media.Path))
        {
            return null;
        }

        return (media.Path, "application/epub+zip");
    }

    private static int ParseSyncTokenOffset(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length >= 2 && parts[0] == "offset" && int.TryParse(parts[1], out var offset))
            {
                return offset;
            }
        }
        catch
        {
            // Ignored, defaults to 0
        }

        return 0;
    }

    private static KoboBookEntitlementContainer ToKoboEntitlementContainer(
        Media media,
        string baseUrl,
        string apiKey,
        ReadingSession? session)
    {
        var cleanBaseUrl = baseUrl.TrimEnd('/');
        var title = media.Metadata?.Title ?? media.Name;
        var summary = media.Metadata?.Summary;

        var contributors = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.Metadata?.Writers))
        {
            contributors.AddRange(media.Metadata.Writers.Split(',').Select(w => w.Trim()));
        }

        var categories = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.Metadata?.Genres))
        {
            categories.AddRange(media.Metadata.Genres.Split(',').Select(g => g.Trim()));
        }

        var entitlement = new KoboBookEntitlement
        {
            Id = media.Id,
            CrossRevisionId = media.Id,
            RevisionId = media.Id,
            Accessibility = "Full",
            Status = "Active",
            Created = media.CreatedAt,
            LastModified = media.UpdatedAt ?? media.CreatedAt,
            ActivePeriod = new KoboPeriod { From = media.CreatedAt }
        };

        var metadata = new KoboBookMetadata
        {
            EntitlementId = media.Id,
            WorkId = media.Id,
            CrossRevisionId = media.Id,
            RevisionId = media.Id,
            CoverImageId = media.Id,
            Title = title,
            Description = summary,
            Contributors = contributors,
            Categories = categories,
            Language = "en",
            Genre = categories.FirstOrDefault() ?? "General",
            DownloadUrls = new List<KoboDownloadUrl>
            {
                new()
                {
                    DrmType = "None",
                    Format = "EPUB",
                    Size = media.Size,
                    Platform = "Generic",
                    Url = $"{cleanBaseUrl}/kobo/{apiKey}/v1/books/{media.Id}/file/epub"
                }
            }
        };

        if (media.Series != null)
        {
            metadata.Series = new KoboSeriesInfo
            {
                Id = media.Series.Id,
                Name = media.Series.Name,
                Number = (media.Metadata?.Number ?? 1m).ToString(),
                NumberFloat = (float)(media.Metadata?.Number ?? 1m)
            };
        }

        KoboReadingState? readingState = null;
        if (session != null)
        {
            var percent = session.EndPercentage.HasValue ? (float?)(session.EndPercentage.Value * 100m) : null;
            readingState = new KoboReadingState
            {
                EntitlementId = media.Id,
                Created = session.CreatedAt,
                LastModified = session.UpdatedAt ?? session.CreatedAt,
                CurrentBookmark = new KoboCurrentBookmark
                {
                    LastModified = session.UpdatedAt ?? session.CreatedAt,
                    ProgressPercent = percent,
                    ContentSourceProgressPercent = percent
                },
                StatusInfo = new KoboStatusInfo
                {
                    LastModified = session.UpdatedAt ?? session.CreatedAt,
                    Status = session.Status == ReadingStatus.Finished ? "Finished" : "Reading",
                    TimesStartedReading = 1
                }
            };
        }

        return new KoboBookEntitlementContainer
        {
            BookEntitlement = entitlement,
            BookMetadata = metadata,
            ReadingState = readingState
        };
    }
}
