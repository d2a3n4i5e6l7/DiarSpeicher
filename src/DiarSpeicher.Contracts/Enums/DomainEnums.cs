namespace DiarSpeicher.Core.Domain.Enums;

public enum FileStatus
{
    Unknown,
    Ready,
    Unsupported,
    Error,
    Missing
}

public static class FileStatusExtensions
{
    public static bool IsRecoveredIfPresent(this FileStatus status) =>
        status is FileStatus.Missing or FileStatus.Unknown;
}

public enum LibraryPattern
{
    SeriesBased,
    CollectionBased
}

public enum LibraryType
{
    Comic,
    Manga,
    Book,
    LightNovel,
    Manhwa,
    Mixed,
    WebNovel,
    Webtoon
}

public enum ReadingStatus
{
    Reading,
    Finished,
    Abandoned,
    NotStarted
}

public enum ReadingMode
{
    Paged,
    ContinuousVertical,
    ContinuousHorizontal
}

public enum ReadingDirection
{
    LeftToRight,
    RightToLeft
}

public enum LibraryViewMode
{
    Grid,
    List
}
