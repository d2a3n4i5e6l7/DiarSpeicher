using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.Catalog;

public sealed class FolderRootDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Declared in configuration but absent on disk. It is still returned, with this set to
    /// false, so the UI can say "that disk is not mounted" instead of hiding it.
    /// </summary>
    [JsonPropertyName("mounted")]
    public bool Mounted { get; set; }
}

public sealed class FolderEntryDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("hasChildren")]
    public bool HasChildren { get; set; }

    /// <summary>
    /// Loose files inside: the hint that a folder already is a library of volumes rather than
    /// another container.
    /// </summary>
    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("readable")]
    public bool Readable { get; set; } = true;
}

public sealed class FolderListingDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Null when going up would leave the allowed folders.</summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("isRoot")]
    public bool IsRoot { get; set; }

    [JsonPropertyName("entries")]
    public List<FolderEntryDto> Entries { get; set; } = [];

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public sealed class DiskUsageDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("freeBytes")]
    public long FreeBytes { get; set; }

    /// <summary>The mount reports no figures, which happens on some network file systems.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }
}

public sealed class CreateFolderInput
{
    [JsonPropertyName("parent")]
    public string Parent { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class PreviewSeriesDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("volumeCount")]
    public int VolumeCount { get; set; }

    [JsonPropertyName("isRoot")]
    public bool IsRoot { get; set; }
}

public sealed class ScanPreviewDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("series")]
    public List<PreviewSeriesDto> Series { get; set; } = [];

    [JsonPropertyName("totalVolumes")]
    public int TotalVolumes { get; set; }
}

public sealed class DeletionPreviewDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("folders")]
    public List<DeletionPreviewDto> Folders { get; set; } = [];
}

public sealed class TrashEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("isDirectory")]
    public bool IsDirectory { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset DeletedAt { get; set; }

    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }
}
