using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.Opds;

public static class OpdsV2MimeTypes
{
    public const string OpdsJson = "application/opds+json";
    public const string PublicationJson = "application/opds-publication+json";
    public const string AuthenticationJson = "application/opds-authentication+json";
}

public class OpdsV2Link
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("rel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rel { get; set; }

    [JsonPropertyName("templated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Templated { get; set; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Properties { get; set; }

    public OpdsV2Link() { }

    public OpdsV2Link(string href, string? type = null, string? rel = null)
    {
        Href = href;
        Type = type;
        Rel = rel;
    }
}

public class OpdsV2Metadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("identifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Identifier { get; set; }

    [JsonPropertyName("@type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("modified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Modified { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Author { get; set; }

    [JsonPropertyName("numberOfPages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NumberOfPages { get; set; }

    [JsonPropertyName("readingProgression")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReadingProgression { get; set; }

    [JsonPropertyName("numberOfItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NumberOfItems { get; set; }

    [JsonPropertyName("itemsPerPage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ItemsPerPage { get; set; }

    [JsonPropertyName("currentPage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("belongsTo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? BelongsTo { get; set; }
}

public class OpdsV2Publication
{
    [JsonPropertyName("@context")]
    public string Context { get; set; } = "https://readium.org/webpub-manifest/context.jsonld";

    [JsonPropertyName("metadata")]
    public OpdsV2Metadata Metadata { get; set; } = new();

    [JsonPropertyName("links")]
    public List<OpdsV2Link> Links { get; set; } = [];

    [JsonPropertyName("images")]
    public List<OpdsV2Link> Images { get; set; } = [];

    [JsonPropertyName("readingOrder")]
    public List<OpdsV2Link> ReadingOrder { get; set; } = [];
}

public class OpdsV2FeedGroup
{
    [JsonPropertyName("metadata")]
    public OpdsV2Metadata Metadata { get; set; } = new();

    [JsonPropertyName("links")]
    public List<OpdsV2Link> Links { get; set; } = [];

    [JsonPropertyName("navigation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpdsV2Link>? Navigation { get; set; }

    [JsonPropertyName("publications")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpdsV2Publication>? Publications { get; set; }
}

public class OpdsV2Feed
{
    [JsonPropertyName("@context")]
    public string Context { get; set; } = "https://readium.org/webpub-manifest/context.jsonld";

    [JsonPropertyName("metadata")]
    public OpdsV2Metadata Metadata { get; set; } = new();

    [JsonPropertyName("links")]
    public List<OpdsV2Link> Links { get; set; } = [];

    [JsonPropertyName("navigation")]
    public List<OpdsV2Link> Navigation { get; set; } = [];

    [JsonPropertyName("publications")]
    public List<OpdsV2Publication> Publications { get; set; } = [];

    [JsonPropertyName("groups")]
    public List<OpdsV2FeedGroup> Groups { get; set; } = [];
}

public class OpdsV2Progression
{
    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("percentage")]
    public decimal? Percentage { get; set; }

    [JsonPropertyName("modified")]
    public string? Modified { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }
}

public class OpdsV2AuthenticationDoc
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "http" + "://opds-spec.org/auth/basic";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "DiarSpeicher OPDS 2.0";

    [JsonPropertyName("links")]
    public List<OpdsV2Link> Links { get; set; } = [];
}
