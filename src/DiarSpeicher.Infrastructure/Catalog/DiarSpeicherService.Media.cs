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
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);
        var ordered = _db.Media.ForUser(user).Include(m => m.Metadata);
        var query = newestFirst
            ? ordered.OrderByDescending(m => m.Id)
            : ordered.OrderBy(m => m.SortName).ThenBy(m => m.Name);

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

    public async Task<ExtractedPage?> GetMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var extracted = await _bookProcessor.ExtractPageAsync(media.Path, page, ct);

        if (extracted != null)
        {
            await TrackReadingProgressAsync(user.Id, mediaId, page, media.Pages, ct);
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

        // The auth middleware synthesises a "default-owner" identity while the server has
        // no users yet. That id has no row in Users, so writing a reading session would
        // violate the foreign key.
        var userExists = await _db.Users.AnyAsync(u => u.Id == user.Id, ct);
        if (!userExists) return false;

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == mediaId)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = mediaId,
                StartPage = 1,
                StartPercentage = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ReadingSessions.Add(session);
        }

        session.EndPage = input.Page;
        if (input.Percentage.HasValue)
        {
            session.EndPercentage = (decimal)input.Percentage.Value;
        }
        else if (media.Pages > 0)
        {
            session.EndPercentage = Math.Clamp((decimal)input.Page / media.Pages, 0m, 1m);
        }

        var isCompleted = input.IsCompleted ?? (session.EndPercentage >= 1.0m || input.Page >= media.Pages);
        session.Status = isCompleted ? ReadingStatus.Finished : ReadingStatus.Reading;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task TrackReadingProgressAsync(string userId, string mediaId, int page, int totalPages, CancellationToken ct)
    {
        try
        {
            // The auth middleware synthesises a "default-owner" identity while the server has
            // no users yet. That id has no row in Users, so writing a reading session would
            // violate the foreign key.
            var userExists = await _db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!userExists) return;

            var session = await _db.ReadingSessions
                .Where(s => s.UserId == userId && s.MediaId == mediaId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);

            if (session == null)
            {
                session = new ReadingSession
                {
                    UserId = userId,
                    MediaId = mediaId,
                    StartPage = page,
                    StartPercentage = totalPages > 0 ? Math.Clamp((decimal)page / totalPages, 0m, 1m) : 0,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.ReadingSessions.Add(session);
            }

            session.EndPage = page;
            session.EndPercentage = totalPages > 0 ? Math.Clamp((decimal)page / totalPages, 0m, 1m) : 0;
            session.Status = (totalPages > 0 && page >= totalPages) ? ReadingStatus.Finished : ReadingStatus.Reading;
            session.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update reading session for user {UserId}, media {MediaId}", userId, mediaId);
        }
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
