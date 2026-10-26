using System.Text.Json.Serialization;
using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Core.Domain.Catalog;

public sealed class DiarSpeicherPageResponse<T>
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

public sealed class DiarSpeicherMediaDto
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
    public DiarSpeicherMediaMetadataDto? Metadata { get; set; }

    [JsonPropertyName("currentPage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }
}

public sealed class DiarSpeicherMediaMetadataDto
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

public sealed class DiarSpeicherSeriesDto
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

    /// <summary>
    /// The client appends it to the thumbnail URL so the browser stops serving the previous
    /// cover from its cache.
    /// </summary>
    [JsonPropertyName("coverUpdatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverUpdatedAt { get; set; }

    /// <summary>
    /// Absent until someone matches the series, which is how the frontend tells an unidentified
    /// series apart.
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiarSpeicherSeriesMetadataDto? Metadata { get; set; }
}

/// <summary>
/// <c>source</c> tells where it came from, so the UI can flag it and offer to revert it.
/// </summary>
public sealed class DiarSpeicherSeriesMetadataDto
{
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonPropertyName("externalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExternalId { get; set; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    [JsonPropertyName("writers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Writers { get; set; }

    [JsonPropertyName("genres")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Genres { get; set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    [JsonPropertyName("year")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Year { get; set; }

    [JsonPropertyName("coverUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; set; }

    [JsonPropertyName("finalVolume")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FinalVolume { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("totalChapters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TotalChapters { get; set; }
}

public sealed class DiarSpeicherLibraryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("missingSeries")]
    public int MissingSeries { get; set; }

    [JsonPropertyName("missingVolumes")]
    public int MissingVolumes { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("seriesCount")]
    public int SeriesCount { get; set; }

    [JsonPropertyName("mediaCount")]
    public int MediaCount { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("emoji")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Emoji { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("lastScannedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastScannedAt { get; set; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiarSpeicherLibraryConfigDto? Config { get; set; }
}

public sealed class DiarSpeicherLibraryConfigDto
{
    [JsonPropertyName("libraryType")]
    public string LibraryType { get; set; } = nameof(Enums.LibraryType.Mixed);

    [JsonPropertyName("libraryPattern")]
    public string LibraryPattern { get; set; } = nameof(Enums.LibraryPattern.SeriesBased);

    [JsonPropertyName("defaultReadingDir")]
    public string DefaultReadingDir { get; set; } = nameof(ReadingDirection.LeftToRight);

    [JsonPropertyName("defaultReadingMode")]
    public string DefaultReadingMode { get; set; } = nameof(ReadingMode.Paged);

    [JsonPropertyName("defaultLibraryViewMode")]
    public string DefaultLibraryViewMode { get; set; } = nameof(LibraryViewMode.Grid);

    [JsonPropertyName("convertRarToZip")]
    public bool ConvertRarToZip { get; set; }

    [JsonPropertyName("hardDeleteConversions")]
    public bool HardDeleteConversions { get; set; }

    [JsonPropertyName("generateFileHashes")]
    public bool GenerateFileHashes { get; set; } = true;

    [JsonPropertyName("generateKoreaderHashes")]
    public bool GenerateKoreaderHashes { get; set; } = true;

    [JsonPropertyName("processMetadata")]
    public bool ProcessMetadata { get; set; } = true;

    [JsonPropertyName("watch")]
    public bool Watch { get; set; }

    [JsonPropertyName("hideSeriesView")]
    public bool HideSeriesView { get; set; }

    [JsonPropertyName("thumbnailWidth")]
    public int ThumbnailWidth { get; set; } = 400;

    [JsonPropertyName("thumbnailHeight")]
    public int ThumbnailHeight { get; set; } = 600;

    [JsonPropertyName("ignoreRules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IgnoreRules { get; set; }
}

public sealed class DiarSpeicherEpubTocDto
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<DiarSpeicherEpubTocItem> Items { get; set; } = new();
}

public sealed class DiarSpeicherEpubTocItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("children")]
    public List<DiarSpeicherEpubTocItem> Children { get; set; } = new();
}

public sealed class DiarSpeicherUpdateProgressInput
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

public sealed class DiarSpeicherSystemStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "OK";

    [JsonPropertyName("semver")]
    public string Semver { get; set; } = "0.1.0";

    [JsonPropertyName("isClaimed")]
    public bool IsClaimed { get; set; }
}

public sealed class DiarSpeicherCreateLibraryInput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }

    [JsonPropertyName("config")]
    public DiarSpeicherLibraryConfigDto? Config { get; set; }
}

/// <summary>
/// A null field means "leave unchanged", so the client can send only what changes without the
/// rest of the resource.
/// </summary>
public sealed class DiarSpeicherUpdateLibraryInput
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Does not copy or move files: it repoints the record to where they already are. The
    /// absolute paths stored in Series and Media are rewritten in the same transaction, because
    /// the scanner matches by path; without that every series would turn "Missing" and reading
    /// progress would hang off duplicated rows.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }

    [JsonPropertyName("config")]
    public DiarSpeicherLibraryConfigDto? Config { get; set; }
}

/// <summary>
/// A null field means "leave unchanged". A series name comes from its folder name, which yields
/// things like "Accel World Tomos [01-08][Completo]" or, when the series hangs off the library
/// root, the library name itself. The scanner only writes it when creating the series, never
/// afterwards, so a manual rename survives rescans.
/// </summary>
public sealed class DiarSpeicherUpdateSeriesInput
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class DiarSpeicherSeriesThumbnailInput
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;
}

public sealed class DiarSpeicherUploadResponseDto
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
    public DiarSpeicherUploadResponseDto? Response { get; init; }
    public string? Message { get; init; }

    public static UploadResult Ok(DiarSpeicherUploadResponseDto response) =>
        new() { Outcome = UploadOutcome.Success, Response = response };

    public static UploadResult Fail(UploadOutcome outcome, string message) =>
        new() { Outcome = outcome, Message = message };
}

public sealed class DiarSpeicherUploadFileInput
{
    public string FileName { get; set; } = string.Empty;
    public Stream Content { get; set; } = Stream.Null;
}

public sealed class DiarSpeicherScanStatusDto
{
    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("completedSeries")]
    public int CompletedSeries { get; set; }

    [JsonPropertyName("totalSeries")]
    public int TotalSeries { get; set; }

    [JsonPropertyName("currentSeries")]
    public string? CurrentSeries { get; set; }

    [JsonPropertyName("completedMedia")]
    public int CompletedMedia { get; set; }

    [JsonPropertyName("totalMedia")]
    public int TotalMedia { get; set; }

    [JsonPropertyName("pendingMedia")]
    public IReadOnlyList<string> PendingMedia { get; set; } = [];

    [JsonPropertyName("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonPropertyName("readingFiles")]
    public IReadOnlyList<string> ReadingFiles { get; set; } = [];

    [JsonPropertyName("createdMedia")]
    public IReadOnlyList<DiarSpeicherMediaDto> CreatedMedia { get; set; } = [];

    [JsonPropertyName("createdSeries")]
    public IReadOnlyList<DiarSpeicherSeriesDto> CreatedSeries { get; set; } = [];

    [JsonPropertyName("currentMedia")]
    public string? CurrentMedia { get; set; }

    [JsonPropertyName("currentSeriesId")]
    public string? CurrentSeriesId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    [JsonPropertyName("elapsedSeconds")]
    public int ElapsedSeconds { get; set; }

    /// <summary>Null until a series has finished: without samples there is no estimate.</summary>
    [JsonPropertyName("etaSeconds")]
    public int? EtaSeconds { get; set; }

    [JsonPropertyName("finished")]
    public bool Finished { get; set; }

    [JsonPropertyName("queued")]
    public bool Queued { get; set; }
}

public sealed class DiarSpeicherMissingSeriesDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("volumeCount")]
    public int VolumeCount { get; set; }

    [JsonPropertyName("readingSessions")]
    public int ReadingSessions { get; set; }
}

public sealed class DiarSpeicherMissingReportDto
{
    [JsonPropertyName("series")]
    public List<DiarSpeicherMissingSeriesDto> Series { get; set; } = [];

    [JsonPropertyName("orphanVolumes")]
    public int OrphanVolumes { get; set; }

    [JsonPropertyName("totalVolumes")]
    public int TotalVolumes { get; set; }

    [JsonPropertyName("totalReadingSessions")]
    public int TotalReadingSessions { get; set; }
}
