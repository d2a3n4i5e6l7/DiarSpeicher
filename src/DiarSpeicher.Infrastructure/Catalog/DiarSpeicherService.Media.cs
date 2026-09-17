namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public async Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> GetMediaAsync(
        AuthUser user,
        int page,
        int pageSize,
        bool newestFirst = false,
        CancellationToken ct = default)
    {
        var ordered = _db.Media.ForUser(user).Include(m => m.Metadata);
        var query = newestFirst
            ? ordered.OrderByDescending(m => m.Id)
            : ordered.OrderBy(m => m.SortName).ThenBy(m => m.Name);

        return await PageMediaAsync(user, query, page, pageSize, ct);
    }

    private async Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> PageMediaAsync(
        AuthUser user,
        IQueryable<Media> query,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        var total = await query.CountAsync(ct);
        var mediaList = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);

        var mediaIds = mediaList.Select(m => m.Id).ToList();
        var sessionMap = await _db.GetLatestSessionsPerMediaAsync(user.Id, mediaIds, ct);

        return new DiarSpeicherPageResponse<DiarSpeicherMediaDto>
        {
            Data = mediaList.Select(m => ToMediaDto(m, sessionMap.GetValueOrDefault(m.Id))).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<DiarSpeicherMediaDto?> GetMediaByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media == null) return null;

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        return ToMediaDto(media, session);
    }

    public async Task<List<DiarSpeicherMediaDto>> GetKeepReadingAsync(AuthUser user, CancellationToken ct = default)
    {
        var sessions = await _db.GetKeepReadingSessionsAsync(user.Id, ct);

        var mediaIds = sessions.Select(s => s.MediaId).Distinct().ToList();

        var mediaList = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .Where(m => mediaIds.Contains(m.Id))
            .ToListAsync(ct);

        var mediaMap = mediaList.ToDictionary(m => m.Id);
        var sessionMap = sessions.ToDictionary(s => s.MediaId);

        var result = new List<DiarSpeicherMediaDto>();
        foreach (var id in mediaIds)
        {
            if (mediaMap.TryGetValue(id, out var media))
            {
                result.Add(ToMediaDto(media, sessionMap.GetValueOrDefault(id)));
            }
        }

        return result;
    }

    public async Task<OpenedPage?> OpenMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var opened = await _bookProcessor.OpenPageAsync(media.Path, page, ct);
        if (opened == null) return null;

        try
        {
            await _progress.RecordAsync(user, media, new ReadingProgressUpdate(page), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update reading session for user {UserId}, media {MediaId}", user.Id, mediaId);
        }

        return opened;
    }

    public async Task<ExtractedPage?> GetMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var extracted = await _bookProcessor.ExtractPageAsync(media.Path, page, ct);

        if (extracted != null)
        {
            try
            {
                await _progress.RecordAsync(user, media, new ReadingProgressUpdate(page), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update reading session for user {UserId}, media {MediaId}", user.Id, mediaId);
            }
        }

        return extracted;
    }

    public async Task<(string Path, string ContentType)?> GetMediaFileAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        return (media.Path, ContentTypeExtensions.FromExtension(media.Extension).ToMimeType());
    }

    public async Task<bool> UpdateProgressAsync(AuthUser user, string mediaId, DiarSpeicherUpdateProgressInput input, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null) return false;

        return await _progress.RecordAsync(
            user,
            media,
            new ReadingProgressUpdate(input.Page, input.IsCompleted, (decimal?)input.Percentage),
            ct);
    }

    public async Task<bool> DeleteMediaAsync(AuthUser user, string id, bool deleteFile = false, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media == null) return false;

        if (deleteFile)
        {
            EnsureTrashed(media.Path, "el fichero");
            await PurgeIndexUnderPathAsync(user, media.Path, ct);

            // La purga por ruta ya se ha llevado esta fila y su miniatura.
            if (await _db.Media.FirstOrDefaultAsync(m => m.Id == id, ct) == null) return true;
        }

        if (!string.IsNullOrWhiteSpace(media.ThumbnailPath))
        {
            TryDeleteFile(media.ThumbnailPath);
        }

        _db.Media.Remove(media);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    private void EnsureTrashed(string path, string what)
    {
        switch (_trash.TryMoveToTrash(path, out _))
        {
            case TrashOutcome.Moved:
            case TrashOutcome.NotFound:
                return;
            case TrashOutcome.NotAllowed:
                throw new InvalidOperationException(
                    $"No se puede borrar {what} del disco: esa ruta esta fuera de las carpetas declaradas en el compose. El indice no se ha tocado.");
            default:
                throw new InvalidOperationException(
                    $"No se pudo mover {what} a la papelera. El indice no se ha tocado.");
        }
    }
}
