namespace DiarSpeicher.Infrastructure.Reading;

public readonly record struct ReadingProgressUpdate(int Page, bool? Completed = null, decimal? Percentage = null);

public readonly record struct ReadingProgressView(int? Page, int TotalPages, bool Reconverted);

public interface IReadingProgress
{
    Task<bool> RecordAsync(AuthUser user, Media book, ReadingProgressUpdate update, CancellationToken ct = default);
    Task<ReadingProgressView> ResolveAsync(Media book, ReadingSession? session, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, int>> GetStoredTotalsAsync(IReadOnlyCollection<string> mediaIds, CancellationToken ct = default);
    static ReadingProgressView FromSession(Media book, ReadingSession? session, int? currentTotal = null) =>
        ReadingProgress.ViewFromSession(book, session, currentTotal);
}
