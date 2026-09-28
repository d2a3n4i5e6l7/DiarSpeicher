using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class ReadingSessionQueryTests : IDisposable
{
    private readonly TestSqliteDatabase _db;

    public ReadingSessionQueryTests()
    {
        _db = new TestSqliteDatabase();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task SeedAsync(params ReadingSession[] sessions)
    {
        var conn = _db.Connection;

        const string insertUserSql = """
            INSERT OR IGNORE INTO "Users" ("Id", "Username", "IsServerOwner", "CreatedAt")
            VALUES (@Id, @Username, 0, @CreatedAt);
            """;

        await conn.ExecuteAsync(insertUserSql, new[]
        {
            new { Id = "u1", Username = "reader", CreatedAt = DateTimeOffset.UtcNow },
            new { Id = "u2", Username = "other", CreatedAt = DateTimeOffset.UtcNow }
        });

        const string insertMediaSql = """
            INSERT OR IGNORE INTO "Media" ("Id", "Name", "Path", "Extension", "Size", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, @Extension, @Size, 'Ready', @CreatedAt);
            """;

        var mediaList = sessions.Select(s => s.MediaId).Distinct().Select(id => new
        {
            Id = id,
            Name = id,
            Path = $"/{id}.cbz",
            Extension = "cbz",
            Size = 1L,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await conn.ExecuteAsync(insertMediaSql, mediaList);

        const string insertSessionSql = """
            INSERT INTO "ReadingSessions" ("MediaId", "UserId", "Status", "EndPage", "SessionDate", "CreatedAt", "UpdatedAt")
            VALUES (@MediaId, @UserId, @Status, @EndPage, @SessionDate, @CreatedAt, @UpdatedAt);
            """;

        var sessionRows = sessions.Select(s => new
        {
            s.MediaId,
            s.UserId,
            Status = s.Status.ToString(),
            s.EndPage,
            SessionDate = s.SessionDate.ToString("yyyy-MM-dd"),
            s.CreatedAt,
            s.UpdatedAt
        });

        await conn.ExecuteAsync(insertSessionSql, sessionRows);
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
            SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
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

        var map = await _db.Connection.GetLatestSessionsPerMediaAsync("u1", new[] { "m1" });

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

        var map = await _db.Connection.GetLatestSessionsPerMediaAsync("u1", new[] { "m1", "m2" });

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

        var map = await _db.Connection.GetLatestSessionsPerMediaAsync("u1", new[] { "m1" });

        Assert.Equal(5, map["m1"].EndPage);
    }

    [Fact]
    public async Task LatestSessionsPerMedia_ReturnsEmptyForAnonymousOrEmptyPage()
    {
        await SeedAsync(Session("m1"));

        Assert.Empty(await _db.Connection.GetLatestSessionsPerMediaAsync(null, new[] { "m1" }));
        Assert.Empty(await _db.Connection.GetLatestSessionsPerMediaAsync("u1", Array.Empty<string>()));
    }

    [Fact]
    public async Task KeepReading_ReturnsEachBookOnceRegardlessOfReadthroughCount()
    {
        await SeedAsync(
            Session("m1", updatedAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)),
            Session("m1", updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Session("m2", updatedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)));

        var sessions = await _db.Connection.GetKeepReadingSessionsAsync("u1");

        Assert.Equal(new[] { "m1", "m2" }, sessions.Select(s => s.MediaId));
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), sessions[0].UpdatedAt);
    }

    [Fact]
    public async Task KeepReading_OrdersByLastTouchedAndFallsBackToCreatedAt()
    {
        await SeedAsync(
            Session("m1"),
            Session("m2", updatedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var sessions = await _db.Connection.GetKeepReadingSessionsAsync("u1");

        Assert.Equal(new[] { "m2", "m1" }, sessions.Select(s => s.MediaId));
    }

    [Fact]
    public async Task KeepReading_ExcludesFinishedBooks()
    {
        await SeedAsync(
            Session("m1", status: ReadingStatus.Finished),
            Session("m2"));

        var sessions = await _db.Connection.GetKeepReadingSessionsAsync("u1");

        Assert.Equal("m2", Assert.Single(sessions).MediaId);
    }
}
