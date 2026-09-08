using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.Sync;

public sealed class KoboBookEntitlementContainer
{
    [JsonPropertyName("BookEntitlement")]
    public KoboBookEntitlement BookEntitlement { get; set; } = null!;

    [JsonPropertyName("BookMetadata")]
    public KoboBookMetadata BookMetadata { get; set; } = null!;

    [JsonPropertyName("ReadingState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KoboReadingState? ReadingState { get; set; }
}

public sealed class KoboBookEntitlement
{
    [JsonPropertyName("Accessibility")]
    public string Accessibility { get; set; } = "Full";

    [JsonPropertyName("ActivePeriod")]
    public KoboPeriod ActivePeriod { get; set; } = new();

    [JsonPropertyName("Created")]
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("CrossRevisionId")]
    public string CrossRevisionId { get; set; } = string.Empty;

    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("IsHiddenFromArchive")]
    public bool IsHiddenFromArchive { get; set; }

    [JsonPropertyName("IsLocked")]
    public bool IsLocked { get; set; }

    [JsonPropertyName("IsRemoved")]
    public bool IsRemoved { get; set; }

    [JsonPropertyName("LastModified")]
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("OriginCategory")]
    public string OriginCategory { get; set; } = "Purchase";

    [JsonPropertyName("RevisionId")]
    public string RevisionId { get; set; } = string.Empty;

    [JsonPropertyName("Status")]
    public string Status { get; set; } = "Active";
}

public sealed class KoboPeriod
{
    [JsonPropertyName("From")]
    public DateTimeOffset From { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KoboBookMetadata
{
    [JsonPropertyName("Categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("Contributors")]
    public List<string> Contributors { get; set; } = new();

    [JsonPropertyName("CoverImageId")]
    public string CoverImageId { get; set; } = string.Empty;

    [JsonPropertyName("CrossRevisionId")]
    public string CrossRevisionId { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("DownloadUrls")]
    public List<KoboDownloadUrl> DownloadUrls { get; set; } = new();

    [JsonPropertyName("EntitlementId")]
    public string EntitlementId { get; set; } = string.Empty;

    [JsonPropertyName("Genre")]
    public string Genre { get; set; } = "Fiction";

    [JsonPropertyName("Language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("RevisionId")]
    public string RevisionId { get; set; } = string.Empty;

    [JsonPropertyName("Series")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KoboSeriesInfo? Series { get; set; }

    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("WorkId")]
    public string WorkId { get; set; } = string.Empty;
}

public sealed class KoboDownloadUrl
{
    [JsonPropertyName("DrmType")]
    public string DrmType { get; set; } = "None";

    [JsonPropertyName("Format")]
    public string Format { get; set; } = "EPUB";

    [JsonPropertyName("Size")]
    public long Size { get; set; }

    [JsonPropertyName("Platform")]
    public string Platform { get; set; } = "Generic";

    [JsonPropertyName("Url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class KoboSeriesInfo
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Number")]
    public string Number { get; set; } = "1";

    [JsonPropertyName("NumberFloat")]
    public float NumberFloat { get; set; } = 1.0f;
}

public sealed class KoboReadingState
{
    [JsonPropertyName("Created")]
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("CurrentBookmark")]
    public KoboCurrentBookmark CurrentBookmark { get; set; } = new();

    [JsonPropertyName("EntitlementId")]
    public string EntitlementId { get; set; } = string.Empty;

    [JsonPropertyName("LastModified")]
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("PriorityTimestamp")]
    public DateTimeOffset PriorityTimestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("Statistics")]
    public KoboStatistics Statistics { get; set; } = new();

    [JsonPropertyName("StatusInfo")]
    public KoboStatusInfo StatusInfo { get; set; } = new();
}

public sealed class KoboCurrentBookmark
{
    [JsonPropertyName("LastModified")]
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("ProgressPercent")]
    public float? ProgressPercent { get; set; }

    [JsonPropertyName("ContentSourceProgressPercent")]
    public float? ContentSourceProgressPercent { get; set; }
}

public sealed class KoboStatistics
{
    [JsonPropertyName("LastModified")]
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KoboStatusInfo
{
    [JsonPropertyName("LastModified")]
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("Status")]
    public string Status { get; set; } = "ReadyToRead";

    [JsonPropertyName("TimesStartedReading")]
    public int TimesStartedReading { get; set; }
}

public sealed class KoboSyncResponse
{
    public List<KoboBookEntitlementContainer> Items { get; set; } = new();
    public string SyncToken { get; set; } = string.Empty;
    public bool ShouldContinue { get; set; }
}
