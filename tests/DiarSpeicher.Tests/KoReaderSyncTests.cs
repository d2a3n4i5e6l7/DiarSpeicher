using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class KoReaderSyncTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _koreaderHash = "koreader-sample-hash-12345";
    private string _mediaId = null!;

    public KoReaderSyncTests()
    {
        _db = new TestSqliteDatabase();
        SeedTestData();
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        conn.Execute("""INSERT INTO "LibraryConfigs" ("Id") VALUES (1);""");

        conn.Execute("""
            INSERT INTO "Users" ("Id", "Username", "IsServerOwner", "CreatedAt")
            VALUES ('guts', 'guts', 1, @Now);
            """, new { Now = DateTimeOffset.UtcNow });

        var libId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Manga', '/media/manga', 'Ready', 1, @Now);
            """, new { Id = libId, Now = DateTimeOffset.UtcNow });

        var seriesId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Berserk', '/media/manga/berserk', @LibraryId, 'Ready', @Now);
            """, new { Id = seriesId, LibraryId = libId, Now = DateTimeOffset.UtcNow });

        _mediaId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "KoreaderHash", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, 'Berserk Vol 01', '/media/manga/berserk/vol01.cbz', 'cbz', 200, @Hash, @SeriesId, 'Ready', @Now);
            """, new { Id = _mediaId, Hash = _koreaderHash, SeriesId = seriesId, Now = DateTimeOffset.UtcNow });

        conn.Execute("""
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "AgeRating")
            VALUES (@MediaId, 'The Black Swordsman', 18);
            """, new { MediaId = _mediaId });
    }

    [Fact]
    public async Task CheckAuthorizedAsync_ReturnsOkResponse()
    {
        var service = new KoReaderService(_db.Factory, NullLogger<KoReaderService>.Instance);

        var result = await service.CheckAuthorizedAsync();

        Assert.Equal("OK", result.Authorized);
    }

    [Fact]
    public async Task UpdateAndGetProgressAsync_FullLifecycle_TracksKoReaderPosition()
    {
        var service = new KoReaderService(_db.Factory, NullLogger<KoReaderService>.Instance);

        var user = new AuthUser
        {
            Id = "guts",
            Username = "guts",
            IsServerOwner = true
        };

        // 1. Initial progress when no session exists
        var initial = await service.GetProgressAsync(user, _koreaderHash);
        Assert.Equal(_koreaderHash, initial.Document);
        Assert.Null(initial.Percentage);
        Assert.Null(initial.Progress);

        // 2. Put reading progress
        var putResult = await service.UpdateProgressAsync(user, new KoReaderProgressInput
        {
            Document = _koreaderHash,
            Progress = "45",
            Percentage = 0.225f,
            Device = "Kobo Clara",
            DeviceId = "kobo-dev-1"
        });

        Assert.Equal(_koreaderHash, putResult.Document);
        Assert.True(putResult.Timestamp > 0);

        // 3. Query progress after update
        var updated = await service.GetProgressAsync(user, _koreaderHash);
        Assert.Equal(_koreaderHash, updated.Document);
        Assert.Equal(0.225f, updated.Percentage);
        Assert.Equal("45", updated.Progress);

        // 4. Verify DB entity
        var session = await _db.Connection.QueryFirstOrDefaultAsync<ReadingSession>(
            """SELECT * FROM "ReadingSessions" WHERE "UserId" = @UserId AND "MediaId" = @MediaId""",
            new { UserId = user.Id, MediaId = _mediaId });
        Assert.NotNull(session);
        Assert.Equal(45, session.EndPage);
        Assert.Equal(0.225m, session.EndPercentage);
        Assert.Equal("45", session.KoreaderProgress);
        Assert.Equal(ReadingStatus.Reading, session.Status);

        // 5. Complete book (100%)
        await service.UpdateProgressAsync(user, new KoReaderProgressInput
        {
            Document = _koreaderHash,
            Progress = "200",
            Percentage = 1.0f,
            Device = "Kobo Clara",
            DeviceId = "kobo-dev-1"
        });

        var finishedSession = await _db.Connection.QueryFirstOrDefaultAsync<ReadingSession>(
            """SELECT * FROM "ReadingSessions" WHERE "UserId" = @UserId AND "MediaId" = @MediaId""",
            new { UserId = user.Id, MediaId = _mediaId });
        Assert.NotNull(finishedSession);
        Assert.Equal(ReadingStatus.Finished, finishedSession.Status);
    }

    [Fact]
    public async Task GetProgressAsync_EnforcesParentalRestrictions()
    {
        var service = new KoReaderService(_db.Factory, NullLogger<KoReaderService>.Instance);

        var childUser = new AuthUser
        {
            Id = "schierke",
            Username = "schierke",
            IsServerOwner = false,
            AgeRestriction = 12
        };

        // Gated by age restriction 12 (book is 18)
        var progress = await service.GetProgressAsync(childUser, _koreaderHash);
        Assert.Null(progress.Percentage);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateProgressAsync(childUser, new KoReaderProgressInput
        {
            Document = _koreaderHash,
            Progress = "1",
            Percentage = 0.01f
        }));
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
