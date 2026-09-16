namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public async Task<List<DiarSpeicherLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default)
    {
        var libraries = await _db.Libraries.ForUser(user)
            .Include(l => l.Series)
            .Include(l => l.Config)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        var libraryIds = libraries.Select(l => l.Id).ToList();

        var counts = await _db.Series.ForUser(user)
            .SelectMany(s => s.Media.Where(m => m.DeletedAt == null),
                (s, m) => new { s.LibraryId, m.Status })
            .Join(libraryIds, x => x.LibraryId, id => id, (x, id) => new { LibraryId = id, x.Status })
            .GroupBy(x => x.LibraryId)
            .Select(g => new
            {
                LibraryId = g.Key,
                Total = g.Count(),
                Missing = g.Count(x => x.Status == FileStatus.Missing)
            })
            .ToDictionaryAsync(x => x.LibraryId, x => x, ct);

        return libraries
            .Select(l =>
            {
                var count = counts.GetValueOrDefault(l.Id);
                var dto = ToLibraryDto(l, count?.Total ?? 0);
                dto.MissingSeries = l.Series.Count(s => s.Status == FileStatus.Missing);
                dto.MissingVolumes = count?.Missing ?? 0;

                return dto;
            })
            .ToList();
    }

    public async Task<DiarSpeicherLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var lib = await _db.Libraries.ForUser(user)
            .Include(l => l.Series)
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (lib == null) return null;

        var mediaCount = await _db.Media
            .CountAsync(m => m.Series != null && m.Series.LibraryId == lib.Id, ct);

        return ToLibraryDto(lib, mediaCount);
    }

    public async Task<bool> TriggerLibraryScanAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        var lib = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == libraryId, ct);

        if (lib == null)
        {
            return false;
        }

        await _scannerQueue.QueueScanAsync(new ScanRequest(libraryId), ct);
        _logger.LogInformation("Enqueued scan task for library {LibraryId} by user {UserId}", libraryId, user.Id);
        return true;
    }

    public async Task<DiarSpeicherLibraryDto?> CreateLibraryAsync(AuthUser user, DiarSpeicherCreateLibraryInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Path))
            return null;

        if (!_libraryRoots.TryResolve(input.Path, out var fullPath))
        {
            throw new InvalidOperationException(
                "Esa ruta esta fuera de las carpetas permitidas para bibliotecas.");
        }

        if (_libraryRoots.IsRoot(fullPath))
        {
            throw new InvalidOperationException(
                "Esa ruta es una raiz de bibliotecas. Elige o crea una carpeta dentro de ella.");
        }

        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        var library = new Library
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name.Trim(),
            Path = fullPath,
            Description = input.Description?.Trim(),
            Emoji = input.Emoji?.Trim(),
            Status = FileStatus.Ready,
            Config = BuildConfig(input.Config)
        };

        _db.Libraries.Add(library);
        await _db.SaveChangesAsync(ct);
        await _scannerQueue.QueueScanAsync(new ScanRequest(library.Id), ct);

        return ToLibraryDto(library, 0);
    }

    public async Task<DiarSpeicherLibraryDto?> UpdateLibraryAsync(AuthUser user, string id, DiarSpeicherUpdateLibraryInput input, CancellationToken ct = default)
    {
        var library = await _db.Libraries.ForUser(user)
            .Include(l => l.Series)
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (library == null) return null;

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            library.Name = input.Name.Trim();
        }

        if (input.Description != null)
        {
            library.Description = input.Description.Trim();
        }

        if (input.Emoji != null)
        {
            library.Emoji = input.Emoji.Trim();
        }

        if (input.Config != null)
        {
            ApplyConfig(library.Config, input.Config);
        }

        var oldPath = library.Path;
        var newPath = ResolveLibraryMove(library, input.Path);

        library.UpdatedAt = DateTimeOffset.UtcNow;

        if (newPath == null)
        {
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            library.Path = newPath;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.SaveChangesAsync(ct);
            await RepointLibraryPathsAsync(library.Id, oldPath, newPath, ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Biblioteca {Library} movida de {OldPath} a {NewPath}", library.Id, oldPath, newPath);
        }

        var mediaCount = await _db.Media
            .CountAsync(m => m.Series != null && m.Series.LibraryId == library.Id, ct);

        return ToLibraryDto(library, mediaCount);
    }

    private string? ResolveLibraryMove(Library library, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;

        if (!_libraryRoots.TryResolve(requested, out var resolved))
        {
            throw new InvalidOperationException(
                "Esa ruta esta fuera de las carpetas permitidas para bibliotecas.");
        }

        if (_libraryRoots.IsRoot(resolved))
        {
            throw new InvalidOperationException(
                "Esa ruta es una raiz de bibliotecas. Elige o crea una carpeta dentro de ella.");
        }

        if (string.Equals(resolved, library.Path, StringComparison.Ordinal)) return null;

        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException("La carpeta destino no existe.");
        }

        return resolved;
    }

    private async Task RepointLibraryPathsAsync(string libraryId, string oldPath, string newPath, CancellationToken ct)
    {
        var cut = oldPath.Length + 1;

        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE Media SET Path = {0} || substr(Path, {1})
            WHERE substr(Path, 1, {2}) = {3}
              AND SeriesId IN (SELECT Id FROM Series WHERE LibraryId = {4})
            """,
            [newPath, cut, oldPath.Length, oldPath, libraryId], ct);

        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE Series SET Path = {0} || substr(Path, {1})
            WHERE substr(Path, 1, {2}) = {3} AND LibraryId = {4}
            """,
            [newPath, cut, oldPath.Length, oldPath, libraryId], ct);

        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ScannedDirectories WHERE substr(Path, 1, {0}) = {1}",
            [oldPath.Length, oldPath], ct);
    }

    public async Task<bool> DeleteLibraryAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default)
    {
        var library = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (library == null) return false;

        if (deleteFiles)
        {
            EnsureTrashed(library.Path, "la carpeta");
            await PurgeIndexUnderPathAsync(user, library.Path, ct);
        }

        await PurgeLibraryContentAsync(library.Id, ct);
        var libraryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(library.Path));
        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ScannedDirectories WHERE substr(Path, 1, {0}) = {1}",
            [libraryPath.Length, libraryPath], ct);

        var configId = library.ConfigId;

        _db.Libraries.Remove(library);
        await _db.SaveChangesAsync(ct);

        var config = await _db.LibraryConfigs.FirstOrDefaultAsync(c => c.Id == configId, ct);
        if (config != null)
        {
            _db.LibraryConfigs.Remove(config);
            await _db.SaveChangesAsync(ct);
        }

        return true;
    }

    private async Task PurgeLibraryContentAsync(string libraryId, CancellationToken ct)
    {
        var series = await _db.Series
            .Where(s => s.LibraryId == libraryId)
            .ToListAsync(ct);

        var media = await _db.Media
            .Where(m => m.Series != null && m.Series.LibraryId == libraryId)
            .ToListAsync(ct);

        var thumbs = media.Select(m => m.ThumbnailPath)
            .Concat(series.Select(s => s.ThumbnailPath))
            .Where(thumb => !string.IsNullOrWhiteSpace(thumb));

        foreach (var thumb in thumbs)
        {
            TryDeleteFile(thumb!);
        }

        _db.Media.RemoveRange(media);
        _db.Series.RemoveRange(series);

        _logger.LogInformation(
            "Purgadas {Series} series y {Media} tomos de la biblioteca {Library}",
            series.Count, media.Count, libraryId);
    }

    private static LibraryConfig BuildConfig(DiarSpeicherLibraryConfigDto? dto)
    {
        var config = new LibraryConfig { LibraryPattern = LibraryPattern.SeriesBased };
        if (dto != null)
        {
            ApplyConfig(config, dto);
        }

        return config;
    }

    private static void ApplyConfig(LibraryConfig config, DiarSpeicherLibraryConfigDto dto)
    {
        config.LibraryType = ParseEnum(dto.LibraryType, config.LibraryType);
        config.LibraryPattern = ParseEnum(dto.LibraryPattern, config.LibraryPattern);
        config.DefaultReadingDir = ParseEnum(dto.DefaultReadingDir, config.DefaultReadingDir);
        config.DefaultReadingMode = ParseEnum(dto.DefaultReadingMode, config.DefaultReadingMode);
        config.DefaultLibraryViewMode = ParseEnum(dto.DefaultLibraryViewMode, config.DefaultLibraryViewMode);
        config.ConvertRarToZip = dto.ConvertRarToZip;
        config.HardDeleteConversions = dto.HardDeleteConversions;
        config.GenerateFileHashes = dto.GenerateFileHashes;
        config.GenerateKoreaderHashes = dto.GenerateKoreaderHashes;
        config.ProcessMetadata = dto.ProcessMetadata;
        config.Watch = dto.Watch;
        config.HideSeriesView = dto.HideSeriesView;
        config.ThumbnailWidth = dto.ThumbnailWidth > 0 ? dto.ThumbnailWidth : config.ThumbnailWidth;
        config.ThumbnailHeight = dto.ThumbnailHeight > 0 ? dto.ThumbnailHeight : config.ThumbnailHeight;
        config.IgnoreRules = string.IsNullOrWhiteSpace(dto.IgnoreRules) ? null : dto.IgnoreRules.Trim();
    }

    private static TEnum ParseEnum<TEnum>(string? raw, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) ? parsed : fallback;
}
