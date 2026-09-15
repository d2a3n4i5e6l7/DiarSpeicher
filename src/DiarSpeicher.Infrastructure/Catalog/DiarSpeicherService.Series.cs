namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public async Task<DiarSpeicherPageResponse<DiarSpeicherSeriesDto>> GetSeriesAsync(AuthUser user, string? libraryId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        var query = _db.Series.ForUser(user).Include(s => s.Metadata).Include(s => s.Media).AsQueryable();

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            query = query.Where(s => s.LibraryId == libraryId);
        }

        query = query.OrderBy(s => s.Name);

        var total = await query.CountAsync(ct);
        var seriesList = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);

        var dtos = seriesList.Select(ToSeriesDto).ToList();

        return new DiarSpeicherPageResponse<DiarSpeicherSeriesDto>
        {
            Data = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<DiarSpeicherSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Include(s => s.Media)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return series == null ? null : ToSeriesDto(series);
    }

    public async Task<DiarSpeicherSeriesDto?> UpdateSeriesAsync(AuthUser user, string id, DiarSpeicherUpdateSeriesInput input, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user)
            .Include(s => s.Metadata)
            .Include(s => s.Media)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (series == null) return null;

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            series.Name = input.Name.Trim();
        }

        if (input.Description != null)
        {
            series.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        }

        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToSeriesDto(series);
    }

    public async Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> GetSeriesMediaAsync(AuthUser user, string seriesId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        var query = _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m => m.SeriesId == seriesId)
            .OrderBy(m => m.SortName).ThenBy(m => m.Name);

        var total = await query.CountAsync(ct);
        var mediaList = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);

        var mediaIds = mediaList.Select(m => m.Id).ToList();
        var sessionMap = await _db.GetLatestSessionsPerMediaAsync(user.Id, mediaIds, ct);

        var dtos = mediaList.Select(m => ToMediaDto(m, sessionMap.GetValueOrDefault(m.Id))).ToList();

        return new DiarSpeicherPageResponse<DiarSpeicherMediaDto>
        {
            Data = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<bool> DeleteSeriesAsync(AuthUser user, string id, bool deleteFiles = false, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series == null) return false;

        var seriesPath = series.Path;

        if (deleteFiles)
        {
            EnsureTrashed(seriesPath, "la carpeta de la serie");
            await PurgeIndexUnderPathAsync(user, seriesPath, ct);
        }

        var remaining = await _db.Series.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (remaining == null) return true;

        if (!string.IsNullOrWhiteSpace(remaining.ThumbnailPath))
        {
            TryDeleteFile(remaining.ThumbnailPath);
        }

        var media = await _db.Media.Where(m => m.SeriesId == remaining.Id).ToListAsync(ct);
        foreach (var item in media)
        {
            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath)) TryDeleteFile(item.ThumbnailPath);
        }

        _db.Media.RemoveRange(media);
        _db.Series.Remove(remaining);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<(byte[] Data, string ContentType)?> GetSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return null;

        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath) && File.Exists(series.ThumbnailPath))
        {
            return await ReadImageAsync(series.ThumbnailPath, ct);
        }

        var fallback = await _db.Media.ForUser(user)
            .Where(m => m.SeriesId == seriesId && m.ThumbnailPath != null)
            .OrderBy(m => m.SortName).ThenBy(m => m.Name)
            .Select(m => m.ThumbnailPath)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(fallback) || !File.Exists(fallback)
            ? null
            : await ReadImageAsync(fallback, ct);
    }

    public async Task<bool> SetSeriesThumbnailFromMediaAsync(AuthUser user, string seriesId, string mediaId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        var source = await _db.Media.ForUser(user)
            .Where(m => m.Id == mediaId)
            .Select(m => m.ThumbnailPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return false;

        var target = BuildSeriesThumbnailPath(seriesId, Path.GetExtension(source));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);

        await ApplySeriesThumbnailAsync(series, target, ct);
        return true;
    }

    public async Task<bool> SetSeriesThumbnailAsync(AuthUser user, string seriesId, Stream image, string fileName, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!CoverExtensions.Contains(extension)) return false;

        var target = BuildSeriesThumbnailPath(seriesId, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await image.CopyToAsync(output, ct);
        }

        await ApplySeriesThumbnailAsync(series, target, ct);
        return true;
    }

    public async Task<bool> ClearSeriesThumbnailAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        var series = await _db.Series.ForUser(user).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath) && File.Exists(series.ThumbnailPath))
        {
            TryDeleteFile(series.ThumbnailPath);
        }

        series.ThumbnailPath = null;
        series.ThumbnailMeta = null;
        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ApplySeriesThumbnailAsync(Series series, string path, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(series.ThumbnailPath)
            && !string.Equals(series.ThumbnailPath, path, StringComparison.Ordinal)
            && File.Exists(series.ThumbnailPath))
        {
            TryDeleteFile(series.ThumbnailPath);
        }

        series.ThumbnailPath = path;
        series.ThumbnailMeta = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private string BuildSeriesThumbnailPath(string seriesId, string extension) =>
        Path.Combine(_storage.ResolveThumbnailsPath(), "series", $"{seriesId}{extension}");

    private static async Task<(byte[] Data, string ContentType)> ReadImageAsync(string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var mime = ContentTypeExtensions.FromExtension(Path.GetExtension(path)).MimeType();
        return (bytes, mime);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "No se pudo borrar la miniatura {Path}", path);
        }
    }
}
