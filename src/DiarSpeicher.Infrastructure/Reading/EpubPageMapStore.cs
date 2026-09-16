using System.Collections.Concurrent;

namespace DiarSpeicher.Infrastructure.Reading;

public class EpubPageMapStore : IEpubPageMapStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Builders = new();

    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<EpubPageMapStore> _logger;

    public EpubPageMapStore(DiarSpeicherDbContext db, ILogger<EpubPageMapStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public static bool IsEpub(Media book) =>
        book.Extension.TrimStart('.').Equals("epub", StringComparison.OrdinalIgnoreCase)
        || book.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);

    public async Task<int> GetTotalPagesAsync(Media book, EpubDeviceProfile profile, CancellationToken ct = default)
    {
        var profileKey = EpubRasterizer.BuildProfileKey(profile);
        var fileModifiedAt = GetFileModifiedAt(book.Path);

        var stored = await FindAsync(book.Id, profileKey, ct);
        if (IsUsable(stored, fileModifiedAt)) return stored!.TotalPages;

        var gate = Builders.GetOrAdd($"{book.Id}:{profileKey}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            stored = await FindAsync(book.Id, profileKey, ct);
            if (IsUsable(stored, fileModifiedAt)) return stored!.TotalPages;

            var measured = await MeasureAsync(book, profile, ct);
            if (measured <= 0)
            {
                return stored?.TotalPages > 0 ? stored.TotalPages : Math.Max(1, book.Pages);
            }

            return await SaveAsync(book, profileKey, measured, fileModifiedAt, stored, profile.Name, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task<EpubPageMap?> FindAsync(string mediaId, string profileKey, CancellationToken ct) =>
        _db.EpubPageMaps.FirstOrDefaultAsync(e => e.MediaId == mediaId && e.ProfileKey == profileKey, ct);

    private static bool IsUsable(EpubPageMap? stored, DateTimeOffset fileModifiedAt) =>
        stored != null && stored.TotalPages > 0 && SameFile(stored.FileModifiedAt, fileModifiedAt);

    private async Task<int> SaveAsync(
        Media book,
        string profileKey,
        int measured,
        DateTimeOffset fileModifiedAt,
        EpubPageMap? stored,
        string profileName,
        CancellationToken ct)
    {
        if (stored == null)
        {
            _db.EpubPageMaps.Add(new EpubPageMap
            {
                MediaId = book.Id,
                ProfileKey = profileKey,
                TotalPages = measured,
                FileModifiedAt = fileModifiedAt,
                BuiltAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            stored.TotalPages = measured;
            stored.FileModifiedAt = fileModifiedAt;
            stored.BuiltAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Otra peticion escribio la fila primero. Su total vale igual que el nuestro:
            // mismo libro y misma maquetacion dan el mismo numero.
            _db.ChangeTracker.Clear();
            var winner = await FindAsync(book.Id, profileKey, ct);
            if (winner?.TotalPages > 0) return winner.TotalPages;

            _logger.LogWarning(ex, "No se pudo guardar el mapa de paginas de {Name}", book.Name);
            return measured;
        }

        _logger.LogInformation(
            "[EPUB] {Total} paginas para {Name} con el perfil {Profile}",
            measured, book.Name, profileName);
        return measured;
    }

    private async Task<int> MeasureAsync(Media book, EpubDeviceProfile profile, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(book.Path)) return 0;

            await using var archive = await ZipFile.OpenReadAsync(book.Path, ct);
            var spine = await EpubBookProcessor.GetSpineEntriesAsync(archive, ct);
            if (spine.Count == 0) spine = EpubBookProcessor.GetFallbackHtmlEntries(archive);
            var cover = await EpubBookProcessor.FindCoverEntryAsync(archive, ct);
            var map = await EpubRasterizer.GetOrBuildPageMapAsync(archive, book.Path, spine, cover, profile, ct);
            return map.TotalPages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo medir el mapa de paginas de {Path}", book.Path);
            return 0;
        }
    }

    private static DateTimeOffset GetFileModifiedAt(string path)
    {
        try
        {
            return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : default;
        }
        catch
        {
            // Sin fecha no se puede saber si el total guardado sigue valiendo: default nunca
            // casa con la comprobacion, asi que el libro se vuelve a medir.
            return default;
        }
    }

    private static bool SameFile(DateTimeOffset stored, DateTimeOffset current) =>
        Math.Abs((stored - current).TotalSeconds) < 1;
}
