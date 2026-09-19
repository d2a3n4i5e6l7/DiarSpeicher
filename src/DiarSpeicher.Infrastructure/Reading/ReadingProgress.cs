namespace DiarSpeicher.Infrastructure.Reading;

public class ReadingProgress : IReadingProgress
{
    private readonly DiarSpeicherDbContext _db;
    private readonly IEpubPageMapStore _pageMaps;
    private readonly IEpubProfileProvider _profiles;
    private readonly ILogger<ReadingProgress> _logger;

    public ReadingProgress(
        DiarSpeicherDbContext db,
        IEpubPageMapStore pageMaps,
        IEpubProfileProvider profiles,
        ILogger<ReadingProgress> logger)
    {
        _db = db;
        _pageMaps = pageMaps;
        _profiles = profiles;
        _logger = logger;
    }

    public static ReadingProgressView ViewFromSession(Media book, ReadingSession? session, int? currentTotal = null)
    {
        if (!EpubPageMapStore.IsEpub(book))
        {
            return new ReadingProgressView(session?.EndPage, Math.Max(1, book.Pages), false);
        }

        return ViewFromEpubSession(book, session, currentTotal);
    }

    private static ReadingProgressView ViewFromEpubSession(Media book, ReadingSession? session, int? currentTotal)
    {
        var storedTotal = session?.RenderedTotalPages ?? 0;
        var fallbackTotal = storedTotal > 0 ? storedTotal : book.Pages;
        var total = currentTotal is > 0 ? currentTotal.Value : Math.Max(1, fallbackTotal);

        if (session?.RenderedPage is not int storedPage)
        {
            return new ReadingProgressView(session?.EndPage, total, false);
        }

        if (storedTotal <= 0 || storedTotal == total)
        {
            return new ReadingProgressView(Math.Clamp(storedPage, 1, total), total, false);
        }

        return new ReadingProgressView(Rescale(storedPage, storedTotal, total), total, true);
    }

    public async Task<bool> RecordAsync(
        AuthUser user,
        Media book,
        ReadingProgressUpdate update,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return false;

        if (!await _db.Users.AnyAsync(u => u.Id == user.Id, ct)) return false;

        var isEpub = EpubPageMapStore.IsEpub(book);
        var profileKey = isEpub ? await CurrentProfileKeyAsync(ct) : null;
        var total = await ResolveTotalAsync(book, ct);
        var page = Math.Clamp(update.Page, 1, Math.Max(1, total));

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == book.Id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = book.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ReadingSessions.Add(session);
        }

        ApplyProgressUpdate(session, isEpub, page, total, profileKey, update);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static void ApplyProgressUpdate(
        ReadingSession session,
        bool isEpub,
        int page,
        int total,
        string? profileKey,
        ReadingProgressUpdate update)
    {
        if (isEpub)
        {
            session.RenderedPage = page;
            session.RenderedTotalPages = total;
            session.RenderedProfileKey = profileKey;
        }
        else
        {
            session.StartPage ??= page;
            session.EndPage = page;
            session.EndPercentage = update.Percentage ?? Percentage(page, total);
            session.StartPercentage ??= 0m;
        }

        session.Status = (update.Completed ?? (total > 0 && page >= total))
            ? ReadingStatus.Finished
            : ReadingStatus.Reading;
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task<ReadingProgressView> ResolveAsync(
        Media book,
        ReadingSession? session,
        CancellationToken ct = default)
    {
        if (!EpubPageMapStore.IsEpub(book))
        {
            return new ReadingProgressView(session?.EndPage, Math.Max(1, book.Pages), false);
        }

        var profile = await _profiles.GetCurrentProfileAsync(ct);
        var profileKey = EpubRasterizer.BuildProfileKey(profile);
        var total = await _pageMaps.GetTotalPagesAsync(book, profile, ct);

        if (session?.RenderedPage is not int storedPage)
        {
            return new ReadingProgressView(session?.EndPage, total, false);
        }

        var storedTotal = session.RenderedTotalPages ?? 0;
        var sameProfile = string.Equals(session.RenderedProfileKey, profileKey, StringComparison.Ordinal);

        if (sameProfile || storedTotal <= 0)
        {
            return new ReadingProgressView(Math.Clamp(storedPage, 1, Math.Max(1, total)), total, false);
        }

        var converted = Rescale(storedPage, storedTotal, total);

        await _db.ReadingSessions
            .Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.RenderedPage, converted)
                    .SetProperty(x => x.RenderedTotalPages, total)
                    .SetProperty(x => x.RenderedProfileKey, profileKey),
                ct);

        session.RenderedPage = converted;
        session.RenderedTotalPages = total;
        session.RenderedProfileKey = profileKey;

        _logger.LogInformation(
            "[EPUB] Progreso de {Name} reconvertido de {OldPage}/{OldTotal} a {NewPage}/{NewTotal} al cambiar de maquetacion",
            book.Name, storedPage, storedTotal, converted, total);

        return new ReadingProgressView(converted, total, true);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStoredTotalsAsync(
        IReadOnlyCollection<string> mediaIds,
        CancellationToken ct = default)
    {
        if (mediaIds.Count == 0) return new Dictionary<string, int>();

        var profileKey = await CurrentProfileKeyAsync(ct);

        return await _db.EpubPageMaps
            .AsNoTracking()
            .Where(e => mediaIds.Contains(e.MediaId) && e.ProfileKey == profileKey && e.TotalPages > 0)
            .ToDictionaryAsync(e => e.MediaId, e => e.TotalPages, ct);
    }

    private async Task<int> ResolveTotalAsync(Media book, CancellationToken ct)
    {
        if (!EpubPageMapStore.IsEpub(book)) return book.Pages;

        var profile = await _profiles.GetCurrentProfileAsync(ct);
        return await _pageMaps.GetTotalPagesAsync(book, profile, ct);
    }

    private async Task<string> CurrentProfileKeyAsync(CancellationToken ct) =>
        EpubRasterizer.BuildProfileKey(await _profiles.GetCurrentProfileAsync(ct));

    private static decimal Percentage(int page, int total) =>
        total > 0 ? Math.Clamp((decimal)page / total, 0m, 1m) : 0m;

    private static int Rescale(int page, int fromTotal, int toTotal) =>
        Math.Clamp(
            (int)Math.Round((decimal)page / fromTotal * toTotal, MidpointRounding.AwayFromZero),
            1,
            Math.Max(1, toTotal));
}
