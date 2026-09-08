using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class KoReaderSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _koreaderHash = "koreader-sample-hash-12345";
    private string _mediaId = null!;

    public KoReaderSyncTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();

        SeedTestData(context);
    }

    private void SeedTestData(DiarSpeicherDbContext context)
    {
        var cfg = new LibraryConfig();
        context.LibraryConfigs.Add(cfg);
        context.SaveChanges();

        var user = new User
        {
            Id = "guts",
            Username = "guts",
            IsServerOwner = true
        };
        context.Users.Add(user);

        var lib = new Library
        {
            Name = "Manga",
            Path = "/media/manga",
            ConfigId = cfg.Id
        };
        context.Libraries.Add(lib);
        context.SaveChanges();

        var series = new Series
        {
            Name = "Berserk",
            Path = "/media/manga/berserk",
            LibraryId = lib.Id
        };
        context.Series.Add(series);
        context.SaveChanges();

        var media = new Media
        {
            Name = "Berserk Vol 01",
            Path = "/media/manga/berserk/vol01.cbz",
            Extension = "cbz",
            Pages = 200,
            KoreaderHash = _koreaderHash,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "The Black Swordsman",
                AgeRating = 18
            }
        };
        context.Media.Add(media);
        context.SaveChanges();
        _mediaId = media.Id;
    }

    [Fact]
    public async Task CheckAuthorizedAsync_ReturnsOkResponse()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoReaderService(context, NullLogger<KoReaderService>.Instance);

        var result = await service.CheckAuthorizedAsync();

        Assert.Equal("OK", result.Authorized);
    }

    [Fact]
    public async Task UpdateAndGetProgressAsync_FullLifecycle_TracksKoReaderPosition()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoReaderService(context, NullLogger<KoReaderService>.Instance);

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
        var session = await context.ReadingSessions.FirstOrDefaultAsync(s => s.UserId == user.Id && s.MediaId == _mediaId);
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

        var finishedSession = await context.ReadingSessions.FirstOrDefaultAsync(s => s.UserId == user.Id && s.MediaId == _mediaId);
        Assert.NotNull(finishedSession);
        Assert.Equal(ReadingStatus.Finished, finishedSession.Status);
    }

    [Fact]
    public async Task GetProgressAsync_EnforcesParentalRestrictions()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoReaderService(context, NullLogger<KoReaderService>.Instance);

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
        _connection.Dispose();
    }
}
