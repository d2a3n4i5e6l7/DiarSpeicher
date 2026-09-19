using System.Text;
using System.Text.Json;
using DiarSpeicher.Core.Domain.Sync;

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
        var sessionMap = await _db.GetLatestSessionsPerMediaAsync(user.Id, mediaIds, ct);

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

    private static KoboBookEntitlement BuildEntitlement(Media media) =>
        new()
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

    private static List<string> ParseCommaList(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static KoboBookMetadata BuildMetadata(Media media, string cleanBaseUrl, string apiKey)
    {
        var title = media.Metadata?.Title ?? media.Name;
        var summary = media.Metadata?.Summary;
        var contributors = ParseCommaList(media.Metadata?.Writers);
        var categories = ParseCommaList(media.Metadata?.Genres);
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
            DownloadUrls =
            [
                new()
                {
                    DrmType = "None",
                    Format = "EPUB",
                    Size = media.Size,
                    Platform = "Generic",
                    Url = $"{cleanBaseUrl}/kobo/{apiKey}/v1/books/{media.Id}/file/epub"
                }
            ],
            Series = BuildSeriesInfo(media)
        };

        return metadata;
    }

    private static KoboSeriesInfo? BuildSeriesInfo(Media media)
    {
        if (media.Series == null) return null;
        var number = media.Metadata?.Number ?? 1m;
        return new KoboSeriesInfo
        {
            Id = media.Series.Id,
            Name = media.Series.Name,
            Number = number.ToString(),
            NumberFloat = (float)number
        };
    }

    private static KoboReadingState? BuildReadingState(string mediaId, ReadingSession? session)
    {
        if (session == null) return null;

        var percent = session.EndPercentage.HasValue ? (float?)(session.EndPercentage.Value * 100m) : null;

        return new KoboReadingState
        {
            EntitlementId = mediaId,
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

    private static KoboBookEntitlementContainer ToKoboEntitlementContainer(
        Media media,
        string baseUrl,
        string apiKey,
        ReadingSession? session)
    {
        var cleanBaseUrl = baseUrl.TrimEnd('/');

        return new KoboBookEntitlementContainer
        {
            BookEntitlement = BuildEntitlement(media),
            BookMetadata = BuildMetadata(media, cleanBaseUrl, apiKey),
            ReadingState = BuildReadingState(media.Id, session)
        };
    }
}
