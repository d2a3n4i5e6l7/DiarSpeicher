using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.StumpV2;

public sealed class StumpPageResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}

public sealed class StumpMediaDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; set; }

    [JsonPropertyName("koreaderHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KoreaderHash { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("seriesId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SeriesId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StumpMediaMetadataDto? Metadata { get; set; }

    [JsonPropertyName("currentPage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }
}

public sealed class StumpMediaMetadataDto
{
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonPropertyName("writers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Writers { get; set; }

    [JsonPropertyName("genre")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Genre { get; set; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    [JsonPropertyName("ageRating")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AgeRating { get; set; }

    [JsonPropertyName("number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Number { get; set; }
}

public sealed class StumpSeriesDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("mediaCount")]
    public int MediaCount { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public sealed class StumpLibraryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("seriesCount")]
    public int SeriesCount { get; set; }
}

public sealed class StumpEpubTocDto
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<StumpEpubTocItem> Items { get; set; } = new();
}

public sealed class StumpEpubTocItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("children")]
    public List<StumpEpubTocItem> Children { get; set; } = new();
}

public sealed class StumpUpdateProgressInput
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("percentage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Percentage { get; set; }

    [JsonPropertyName("isCompleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsCompleted { get; set; }
}

public sealed class StumpSystemStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "OK";

    [JsonPropertyName("semver")]
    public string Semver { get; set; } = "0.1.0";

    [JsonPropertyName("isClaimed")]
    public bool IsClaimed { get; set; }
}

public sealed class StumpCreateLibraryInput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class StumpUploadResponseDto
{
    [JsonPropertyName("uploadedCount")]
    public int UploadedCount { get; set; }

    [JsonPropertyName("files")]
    public List<UploadedFileDto> Files { get; set; } = new();

    [JsonPropertyName("scanJobTriggered")]
    public bool ScanJobTriggered { get; set; }
}

public sealed class UploadedFileDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public enum UploadOutcome
{
    Success,
    UploadDisabled,
    PermissionDenied,
    LibraryNotFound,
    NoAcceptedFiles,
    FileTooLarge,
    ExtensionNotAllowed
}

public sealed class UploadResult
{
    public UploadOutcome Outcome { get; init; }
    public StumpUploadResponseDto? Response { get; init; }
    public string? Message { get; init; }

    public static UploadResult Ok(StumpUploadResponseDto response) =>
        new() { Outcome = UploadOutcome.Success, Response = response };

    public static UploadResult Fail(UploadOutcome outcome, string message) =>
        new() { Outcome = outcome, Message = message };
}

public sealed class StumpUploadFileInput
{
    public string FileName { get; set; } = string.Empty;
    public Stream Content { get; set; } = Stream.Null;
}


