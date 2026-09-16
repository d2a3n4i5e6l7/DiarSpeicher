namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Reconciliacion entre lo indexado y lo que queda en disco: que falta y como purgarlo.
/// </summary>
public sealed partial class DiarSpeicherService
{
    public async Task<DiarSpeicherMissingReportDto> GetMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Where(s => s.LibraryId == libraryId && s.Status == FileStatus.Missing)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Path,
                Volumes = s.Media.Count,
                Sessions = s.Media.Sum(m => m.ReadingSessions.Count)
            })
            .ToListAsync(ct);

        var report = new DiarSpeicherMissingReportDto
        {
            Series = series.Select(s => new DiarSpeicherMissingSeriesDto
            {
                Id = s.Id,
                Name = s.Name,
                Path = s.Path,
                VolumeCount = s.Volumes,
                ReadingSessions = s.Sessions
            }).ToList()
        };

        var orphans = await _db.Media.ForUser(user)
            .Where(m => m.Series != null
                && m.Series.LibraryId == libraryId
                && m.Status == FileStatus.Missing
                && m.Series.Status != FileStatus.Missing)
            .Select(m => new { Sessions = m.ReadingSessions.Count })
            .ToListAsync(ct);

        report.OrphanVolumes = orphans.Count;
        report.TotalVolumes = report.Series.Sum(s => s.VolumeCount) + orphans.Count;
        report.TotalReadingSessions = report.Series.Sum(s => s.ReadingSessions) + orphans.Sum(o => o.Sessions);

        return report;
    }

    /// <summary>
    /// Quita del indice lo que ya no esta en disco. Nunca toca un fichero.
    /// </summary>
    public async Task<int> PurgeMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Where(s => s.LibraryId == libraryId && s.Status == FileStatus.Missing)
            .ToListAsync(ct);

        var goneSeries = series.Where(s => !Directory.Exists(s.Path)).ToList();

        var media = await _db.Media.ForUser(user)
            .Where(m => m.Series != null && m.Series.LibraryId == libraryId && m.Status == FileStatus.Missing)
            .ToListAsync(ct);

        var goneMedia = media.Where(m => !File.Exists(m.Path)).ToList();

        foreach (var item in goneMedia.Where(item => !string.IsNullOrWhiteSpace(item.ThumbnailPath)))
        {
            TryDeleteFile(item.ThumbnailPath!);
        }

        foreach (var s in goneSeries.Where(s => !string.IsNullOrWhiteSpace(s.ThumbnailPath)))
        {
            TryDeleteFile(s.ThumbnailPath!);
        }

        _db.Media.RemoveRange(goneMedia);
        _db.Series.RemoveRange(goneSeries);
        await _db.SaveChangesAsync(ct);

        var removed = goneSeries.Count + goneMedia.Count;
        _logger.LogInformation(
            "Purgadas {Count} entradas perdidas de la biblioteca {Library}", removed, libraryId);

        return removed;
    }

    public async Task<int> PurgeIndexUnderPathAsync(AuthUser user, string path, CancellationToken ct = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix = root + Path.DirectorySeparatorChar;

        var media = await _db.Media.ForUser(user)
            .Where(m => m.Path == root || m.Path.StartsWith(prefix))
            .ToListAsync(ct);

        var series = await _db.Series.ForUser(user)
            .Where(s => s.Path == root || s.Path.StartsWith(prefix))
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
        await _db.SaveChangesAsync(ct);

        var removed = media.Count + series.Count;
        if (removed > 0)
        {
            _logger.LogInformation("Sacadas {Count} entradas del indice bajo {Path}", removed, root);
        }

        return removed;
    }
}
