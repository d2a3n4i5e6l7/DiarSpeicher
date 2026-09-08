namespace DiarSpeicher.Core.Domain.Opds;

public static class OpdsNamespaces
{
    private const string HttpScheme = "http" + "://";

    public const string Atom = HttpScheme + "www.w3.org/2005/Atom";
    public const string Opds = HttpScheme + "opds-spec.org/2010/catalog";
    public const string Pse = HttpScheme + "vaemendis.net/opds-pse/ns";
    public const string OpenSearch = HttpScheme + "a9.com/-/spec/opensearch/1.1/";
}

public static class OpdsLinkType
{
    public const string Acquisition = "application/atom+xml;profile=opds-catalog;kind=acquisition";
    public const string Navigation = "application/atom+xml;profile=opds-catalog;kind=navigation";
    public const string ImageJpeg = "image/jpeg";
    public const string ImagePng = "image/png";
    public const string ImageGif = "image/gif";
    public const string OctetStream = "application/octet-stream";
    public const string Zip = "application/zip";
    public const string Epub = "application/epub+zip";
    public const string Search = "application/opensearchdescription+xml";

    public static string FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return OctetStream;
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpeg" or "jpg" => ImageJpeg,
            "png" => ImagePng,
            "gif" => ImageGif,
            "epub" => Epub,
            "zip" or "cbz" or "rar" or "cbr" => Zip,
            _ => OctetStream
        };
    }
}

public static class OpdsLinkRel
{
    private const string HttpScheme = "http" + "://";

    public const string ItSelf = "self";
    public const string Subsection = "subsection";
    public const string Acquisition = HttpScheme + "opds-spec.org/acquisition";
    public const string Start = "start";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Thumbnail = HttpScheme + "opds-spec.org/image/thumbnail";
    public const string Image = HttpScheme + "opds-spec.org/image";
    public const string PageStream = HttpScheme + "vaemendis.net/opds-pse/stream";
    public const string Search = "search";
}

public class OpdsLink
{
    public string Type { get; set; } = OpdsLinkType.Navigation;
    public string Rel { get; set; } = OpdsLinkRel.ItSelf;
    public string Href { get; set; } = string.Empty;

    public OpdsLink() { }

    public OpdsLink(string type, string rel, string href)
    {
        Type = type;
        Rel = rel;
        Href = href;
    }
}

public class OpdsStreamLink
{
    public string BookId { get; set; } = string.Empty;
    public int Count { get; set; }
    public string MimeType { get; set; } = OpdsLinkType.ImageJpeg;
    public int? LastRead { get; set; }
    public string? LastReadDate { get; set; }
    public string Href { get; set; } = string.Empty;

    public OpdsStreamLink() { }

    public OpdsStreamLink(string bookId, int count, string mimeType, int? lastRead, string? lastReadDate, string href)
    {
        BookId = bookId;
        Count = count;
        MimeType = mimeType;
        LastRead = lastRead;
        LastReadDate = lastReadDate;
        Href = href;
    }
}

public class OpdsAuthor
{
    public string Name { get; set; } = "DiarSpeicher";
    public string? Uri { get; set; } = "https://github.com/diarmund/DiarSpeicher";

    public OpdsAuthor() { }

    public OpdsAuthor(string name, string? uri = null)
    {
        Name = name;
        Uri = uri;
    }
}

public class OpdsEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public List<string>? Authors { get; set; }
    public List<OpdsLink> Links { get; set; } = [];
    public OpdsStreamLink? StreamLink { get; set; }
}

public class OpdsFeed
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public OpdsAuthor Author { get; set; } = new();
    public List<OpdsLink> Links { get; set; } = [];
    public List<OpdsEntry> Entries { get; set; } = [];

    public OpdsFeed() { }

    public OpdsFeed(string id, string title, List<OpdsLink>? links = null, List<OpdsEntry>? entries = null)
    {
        Id = id;
        Title = title;
        if (links != null) Links = links;
        if (entries != null) Entries = entries;
    }
}
