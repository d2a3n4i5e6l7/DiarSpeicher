using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Sync;

public sealed class KoReaderService : IKoReaderService
{
    private readonly DiarSpeicherDbContext _db;
    private readonly ILogger<KoReaderService> _logger;

    public KoReaderService(DiarSpeicherDbContext db, ILogger<KoReaderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<KoReaderAuthResponse> CheckAuthorizedAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new KoReaderAuthResponse { Authorized = "OK" });
    }

    public async Task<KoReaderProgressResponse> GetProgressAsync(AuthUser user, string koreaderHash, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.KoreaderHash == koreaderHash, ct);

        if (media == null)
        {
            _logger.LogDebug("Media with KOReader hash {Hash} not found for user {UserId}", koreaderHash, user.Id);
            return new KoReaderProgressResponse { Document = koreaderHash };
        }

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == media.Id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            return new KoReaderProgressResponse { Document = koreaderHash };
        }

        var timestamp = (ulong)(session.UpdatedAt ?? session.CreatedAt).ToUnixTimeMilliseconds();

        return new KoReaderProgressResponse
        {
            Document = koreaderHash,
            Percentage = session.EndPercentage.HasValue ? (float)session.EndPercentage.Value : null,
            Progress = session.KoreaderProgress ?? session.EndPage?.ToString(),
            Timestamp = timestamp
        };
    }

    public async Task<KoReaderPutProgressResponse> UpdateProgressAsync(AuthUser user, KoReaderProgressInput input, CancellationToken ct = default)
    {
        if (input.Percentage < 0.0f || input.Percentage > 1.0f)
        {
            throw new ArgumentException("Invalid percentage", nameof(input));
        }

        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.KoreaderHash == input.Document, ct);

        if (media == null)
        {
            throw new KeyNotFoundException($"Media with KOReader hash '{input.Document}' not found");
        }

        var session = await _db.ReadingSessions
            .Where(s => s.UserId == user.Id && s.MediaId == media.Id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = media.Id,
                StartPage = 1,
                StartPercentage = 0,
                Status = ReadingStatus.Reading,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ReadingSessions.Add(session);
        }

        session.EndPercentage = (decimal)input.Percentage;
        session.KoreaderProgress = input.Progress;

        if (int.TryParse(input.Progress, out var page))
        {
            session.EndPage = page;
        }

        session.Status = input.Percentage >= 1.0f ? ReadingStatus.Finished : ReadingStatus.Reading;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new KoReaderPutProgressResponse
        {
            Document = input.Document,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
