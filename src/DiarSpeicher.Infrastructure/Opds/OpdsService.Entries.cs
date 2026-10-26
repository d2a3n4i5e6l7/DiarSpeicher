using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Infrastructure.Reading;

namespace DiarSpeicher.Infrastructure.Opds;

public partial class OpdsService
{
    private OpdsEntry ToOpdsEntry(Library library, string? apiKey)
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

    private OpdsEntry ToOpdsEntry(Series series, string? apiKey)
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

    private OpdsEntry ToOpdsEntry(
        Media media,
        ReadingSession? session,
        string? apiKey,
        IReadOnlyDictionary<string, int>? epubTotals = null)
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
            new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Image, FormatUrl($"books/{media.Id}/pages/0?zero_based=true&cover=1", apiKey)),
            new(OpdsLinkType.FromExtension(media.Extension), OpdsLinkRel.Acquisition, FormatUrl($"books/{media.Id}/file/{fileName}", apiKey))
        };

        var progress = IReadingProgress.FromSession(media, session, epubTotals.TotalFor(media.Id));

        var streamLink = new OpdsStreamLink(
            media.Id,
            progress.TotalPages,
            OpdsLinkType.ImageJpeg,
            progress.Page,
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

    private OpdsFeed CreatePaginatedFeed(PaginatedFeedParams p)
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
}
