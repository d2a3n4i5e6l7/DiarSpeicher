using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Tests;

public sealed class ReadingSessionQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    public ReadingSessionQueryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private DiarSpeicherDbContext NewContext() => new(_options);

    private async Task SeedAsync(params ReadingSession[] sessions)
    {
        await using var db = NewContext();
        db.Users.Add(new User { Id = "u1", Username = "reader" });
        db.Users.Add(new User { Id = "u2", Username = "other" });

        foreach (var mediaId in sessions.Select(s => s.MediaId).Distinct())
        {
            db.Media.Add(new Media
            {
                Id = mediaId,
                Name = mediaId,
                Path = $"/{mediaId}.cbz",
                Extension = "cbz",
                Size = 1
            });
        }

        db.ReadingSessions.AddRange(sessions);
        await db.SaveChangesAsync();
    }

    private static ReadingSession Session(
        string mediaId,
        ReadingStatus status = ReadingStatus.Reading,
        string userId = "u1",
        DateTimeOffset? updatedAt = null,
        int endPage = 0) => new()
        {
            MediaId = mediaId,
            UserId = userId,
            Status = status,
            EndPage = endPage,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = updatedAt
        };

    [Fact]
    public async Task LatestSessionsPerMedia_ReturnsOneRowForARereadBook()
    {
        await SeedAsync(
            Session("m1", endPage: 10),
            Session("m1", endPage: 20),
            Session("m1", endPage: 30));

        await using var db = NewContext();
        var map = await db.GetLatestSessionsPerMediaAsync("u1", new[] { "m1" });

        Assert.Single(map);
        Assert.Equal(30, map["m1"].EndPage);
    }

    [Fact]
    public async Task LatestSessionsPerMedia_DoesNotThrowOnDuplicateMediaIds()
    {
        await SeedAsync(
            Session("m1"),
            Session("m1"),
            Session("m2"));

        await using var db = NewContext();
        var map = await db.GetLatestSessionsPerMediaAsync("u1", new[] { "m1", "m2" });

        Assert.Equal(2, map.Count);
        Assert.Equal("m1", map["m1"].MediaId);
        Assert.Equal("m2", map["m2"].MediaId);
    }

    [Fact]
    public async Task LatestSessionsPerMedia_IgnoresOtherUsers()
    {
        await SeedAsync(
            Session("m1", userId: "u1", endPage: 5),
            Session("m1", userId: "u2", endPage: 99));

        await using var db = NewContext();
        var map = await db.GetLatestSessionsPerMediaAsync("u1", new[] { "m1" });

        Assert.Equal(5, map["m1"].EndPage);
    }

    [Fact]
    public async Task LatestSessionsPerMedia_ReturnsEmptyForAnonymousOrEmptyPage()
    {
        await SeedAsync(Session("m1"));

        await using var db = NewContext();

        Assert.Empty(await db.GetLatestSessionsPerMediaAsync(null, new[] { "m1" }));
        Assert.Empty(await db.GetLatestSessionsPerMediaAsync("u1", Array.Empty<string>()));
    }

    [Fact]
    public async Task KeepReading_ReturnsEachBookOnceRegardlessOfReadthroughCount()
    {
        await SeedAsync(
            Session("m1", updatedAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            Session("m1", updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Session("m2", updatedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)));

        await using var db = NewContext();
        var sessions = await db.GetKeepReadingSessionsAsync("u1");

        Assert.Equal(new[] { "m1", "m2" }, sessions.Select(s => s.MediaId));
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), sessions[0].UpdatedAt);
    }

    [Fact]
    public async Task KeepReading_OrdersByLastTouchedAndFallsBackToCreatedAt()
    {
        await SeedAsync(
            Session("m1"),
            Session("m2", updatedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        await using var db = NewContext();
        var sessions = await db.GetKeepReadingSessionsAsync("u1");

        Assert.Equal(new[] { "m2", "m1" }, sessions.Select(s => s.MediaId));
    }

    [Fact]
    public async Task KeepReading_ExcludesFinishedBooks()
    {
        await SeedAsync(
            Session("m1", status: ReadingStatus.Finished),
            Session("m2"));

        await using var db = NewContext();
        var sessions = await db.GetKeepReadingSessionsAsync("u1");

        Assert.Equal("m2", Assert.Single(sessions).MediaId);
    }
}
