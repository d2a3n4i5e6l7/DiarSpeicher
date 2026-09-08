namespace DiarSpeicher.Core.Filesystem;

public record WalkedLibrary
{
    public ulong SeenDirectories { get; init; }
    public ulong IgnoredDirectories { get; init; }
    public List<string> SeriesToCreate { get; init; } = [];
    public List<string> RecoveredSeries { get; init; } = [];
    public List<string> SeriesToVisit { get; init; } = [];
    public List<string> MissingSeries { get; init; } = [];
    public bool LibraryIsMissing { get; init; }

    public static WalkedLibrary Missing() => new() { LibraryIsMissing = true };
}

public record WalkedSeries
{
    public ulong SeenFiles { get; init; }
    public ulong IgnoredFiles { get; init; }
    public ulong SkippedFiles { get; init; }
    public List<string> MediaToCreate { get; init; } = [];
    public List<string> RecoveredMedia { get; init; } = [];
    public List<string> MediaToVisit { get; init; } = [];
    public List<string> MissingMedia { get; init; } = [];
    public bool SeriesIsMissing { get; init; }
    public Dictionary<string, long> ObservedDirMtimes { get; init; } = new();

    public static WalkedSeries Missing() => new() { SeriesIsMissing = true };
}

public record LibraryScanReport
{
    public string LibraryId { get; init; } = string.Empty;
    public ulong TotalDirectories { get; set; }
    public ulong TotalFiles { get; set; }
    public ulong IgnoredDirectories { get; set; }
    public ulong IgnoredFiles { get; set; }
    public ulong SkippedFiles { get; set; }
    public ulong CreatedSeries { get; set; }
    public ulong UpdatedSeries { get; set; }
    public ulong CreatedMedia { get; set; }
    public ulong UpdatedMedia { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public record ScanRequest(string LibraryId);
