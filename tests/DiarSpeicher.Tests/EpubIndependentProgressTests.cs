using Dapper;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Komga;
using DiarSpeicher.Infrastructure.Opds;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class EpubIndependentProgressTests : IDisposable
{
    private const string LibraryId = "lib";
    private const string SeriesId = "saga";
    private const string FirstEpub = "epub1";
    private const string SecondEpub = "epub2";
    private const string Cbz = "cbz1";
    private const string FirstHash = "hash-epub-1";
    private const string SecondHash = "hash-epub-2";
    private const int EpubPages = 300;
    private const int CbzPages = 10;
    private const float HalfWay = 0.5f;
    private const int ImagePage = 200;

    private readonly TestSqliteDatabase _db = new();
    private readonly KomgaService _komga;
    private readonly KoReaderService _koreader;
    private readonly OpdsV2Service _opds;
    private readonly DiarSpeicherService _web;

    private readonly AuthUser _sister = new() { Id = "sister", Username = "sister" };
    private readonly AuthUser _brother = new() { Id = "brother", Username = "brother" };

    public EpubIndependentProgressTests()
    {
        var progress = TestReadingProgress.For(_db.Factory);
        _komga = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, progress);
        _koreader = new KoReaderService(_db.Factory, NullLogger<KoReaderService>.Instance);
        _opds = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, progress);
        _web = new DiarSpeicherService(
            _db.Factory,
            new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() }),
            new ScannerQueue(),
            NullLogger<DiarSpeicherService>.Instance,
            TestStorageOptions.Default(),
            progress);
        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        var conn = _db.Connection;
        var now = DateTimeOffset.UtcNow;

        conn.Execute("""INSERT INTO "LibraryConfigs" ("Id") VALUES (1);""");
        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Books', '/data/books', 'Ready', 1, @Now);
            """, new { Id = LibraryId, Now = now });
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Saga', '/data/books/saga', @LibraryId, 'Ready', @Now);
            """, new { Id = SeriesId, LibraryId, Now = now });
        conn.Execute("""
            INSERT INTO "Users" ("Id", "Username", "CreatedAt") VALUES (@Id, @Username, @Now);
            """, new[]
        {
            new { _sister.Id, _sister.Username, Now = now },
            new { _brother.Id, _brother.Username, Now = now }
        });

        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "Size", "KoreaderHash", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, @Extension, @Pages, 1000, @Hash, @SeriesId, 'Ready', @Now);
            """, new[]
        {
            new { Id = FirstEpub, Name = "Saga 01", Path = "/data/books/saga/01.epub", Extension = "epub", Pages = EpubPages, Hash = (string?)FirstHash, SeriesId, Now = now },
            new { Id = SecondEpub, Name = "Saga 02", Path = "/data/books/saga/02.epub", Extension = "epub", Pages = EpubPages, Hash = (string?)SecondHash, SeriesId, Now = now },
            new { Id = Cbz, Name = "Saga 03", Path = "/data/books/saga/03.cbz", Extension = "cbz", Pages = CbzPages, Hash = (string?)null, SeriesId, Now = now }
        });
    }

    private Task<bool> ReadAsImage(AuthUser user, string bookId, int page, bool? completed = null) =>
        _komga.UpdateReadProgressAsync(user, bookId, page, completed);

    private Task<KoReaderPutProgressResponse> ReadAsPercentage(AuthUser user, string hash, float percentage) =>
        _koreader.UpdateProgressAsync(user, new KoReaderProgressInput
        {
            Document = hash,
            Progress = "/body/DocFragment[3]/p[7]",
            Percentage = percentage
        });

    private async Task<ReadingSession> SessionOf(AuthUser user, string bookId) =>
        await _db.Connection.QuerySingleAsync<ReadingSession>(
            """SELECT * FROM "ReadingSessions" WHERE "UserId" = @UserId AND "MediaId" = @MediaId ORDER BY "Id" DESC LIMIT 1;""",
            new { UserId = user.Id, MediaId = bookId });

    private async Task<int> SessionCount(AuthUser user, string bookId) =>
        await _db.Connection.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "ReadingSessions" WHERE "UserId" = @UserId AND "MediaId" = @MediaId;""",
            new { UserId = user.Id, MediaId = bookId });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PercentageAndImagePage_KeepTheirOwnValues_WhicheverIsWrittenFirst(bool percentageFirst)
    {
        if (percentageFirst)
        {
            await ReadAsPercentage(_sister, FirstHash, HalfWay);
            await ReadAsImage(_sister, FirstEpub, ImagePage);
        }
        else
        {
            await ReadAsImage(_sister, FirstEpub, ImagePage);
            await ReadAsPercentage(_sister, FirstHash, HalfWay);
        }

        var session = await SessionOf(_sister, FirstEpub);
        Assert.Equal(1, await SessionCount(_sister, FirstEpub));
        Assert.Equal(0.5m, session.EndPercentage);
        Assert.Equal("/body/DocFragment[3]/p[7]", session.KoreaderProgress);
        Assert.Equal(ReadingStatus.Reading, session.Status);
        Assert.Equal(ImagePage, session.RenderedPage);
        Assert.Equal(EpubPages, session.RenderedTotalPages);
        Assert.Equal(nameof(ReadingStatus.Reading), session.RenderedStatus);

        var koreader = await _koreader.GetProgressAsync(_sister, FirstHash);
        Assert.Equal(0.5f, koreader.Percentage);
        Assert.Equal("/body/DocFragment[3]/p[7]", koreader.Progress);

        var komga = (await _komga.GetBookByIdAsync(_sister, FirstEpub))!.ReadProgress!;
        Assert.Equal(ImagePage, komga.Page);
        Assert.False(komga.Completed);

        var web = (await _web.GetMediaByIdAsync(_sister, FirstEpub))!;
        Assert.Equal(50.0, web.ReadPercent);
        Assert.False(web.IsCompleted);
        Assert.Equal(ImagePage, web.RenderedPage);
        Assert.Equal(EpubPages, web.RenderedTotalPages);
        Assert.False(web.RenderedCompleted);
    }

    [Fact]
    public async Task FinishingInImageMode_DoesNotFinishThePercentageTrack()
    {
        await ReadAsPercentage(_sister, FirstHash, HalfWay);
        await ReadAsImage(_sister, FirstEpub, EpubPages);

        var session = await SessionOf(_sister, FirstEpub);
        Assert.Equal(nameof(ReadingStatus.Finished), session.RenderedStatus);
        Assert.Equal(EpubPages, session.RenderedPage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
        Assert.Equal(0.5m, session.EndPercentage);

        Assert.True((await _komga.GetBookByIdAsync(_sister, FirstEpub))!.ReadProgress!.Completed);
        Assert.Equal(0.5f, (await _koreader.GetProgressAsync(_sister, FirstHash)).Percentage);

        var web = (await _web.GetMediaByIdAsync(_sister, FirstEpub))!;
        Assert.False(web.IsCompleted);
        Assert.True(web.RenderedCompleted);
    }

    [Fact]
    public async Task FinishingByPercentage_DoesNotFinishTheImageTrack()
    {
        await ReadAsImage(_sister, FirstEpub, ImagePage);
        await ReadAsPercentage(_sister, FirstHash, 1.0f);

        var session = await SessionOf(_sister, FirstEpub);
        Assert.Equal(ReadingStatus.Finished, session.Status);
        Assert.Equal(1.0m, session.EndPercentage);
        Assert.Equal(nameof(ReadingStatus.Reading), session.RenderedStatus);
        Assert.Equal(ImagePage, session.RenderedPage);

        var komga = (await _komga.GetBookByIdAsync(_sister, FirstEpub))!.ReadProgress!;
        Assert.False(komga.Completed);
        Assert.Equal(ImagePage, komga.Page);

        var series = (await _komga.GetSeriesByIdAsync(_sister, SeriesId))!;
        Assert.Equal(0, series.BooksReadCount);
        Assert.Equal(1, series.BooksInProgressCount);

        var web = (await _web.GetMediaByIdAsync(_sister, FirstEpub))!;
        Assert.True(web.IsCompleted);
        Assert.False(web.RenderedCompleted);
    }

    [Fact]
    public async Task PercentageOnlyProgress_IsInvisibleToImageProtocols()
    {
        await ReadAsPercentage(_sister, FirstHash, HalfWay);

        Assert.Null((await _komga.GetBookByIdAsync(_sister, FirstEpub))!.ReadProgress);
        Assert.Null(await _opds.GetProgressionAsync(_sister, FirstEpub));

        var series = (await _komga.GetSeriesByIdAsync(_sister, SeriesId))!;
        Assert.Equal(0, series.BooksReadCount);
        Assert.Equal(0, series.BooksInProgressCount);
        Assert.Equal(3, series.BooksUnreadCount);

        var web = (await _web.GetMediaByIdAsync(_sister, FirstEpub))!;
        Assert.Equal(50.0, web.ReadPercent);
        Assert.Null(web.RenderedPage);
        Assert.Null(web.RenderedCompleted);
    }

    [Fact]
    public async Task ImageOnlyProgress_IsInvisibleToThePercentageProtocols()
    {
        await ReadAsImage(_sister, FirstEpub, ImagePage);

        var koreader = await _koreader.GetProgressAsync(_sister, FirstHash);
        Assert.Null(koreader.Percentage);
        Assert.Null(koreader.Progress);

        var web = (await _web.GetMediaByIdAsync(_sister, FirstEpub))!;
        Assert.Null(web.ReadPercent);
        Assert.Null(web.CurrentPage);
        Assert.False(web.IsCompleted);
        Assert.Equal(ImagePage, web.RenderedPage);
    }

    [Fact]
    public async Task OpdsProgression_ReportsTheImagePageNeverThePercentage()
    {
        await ReadAsPercentage(_sister, FirstHash, HalfWay);
        await ReadAsImage(_sister, FirstEpub, ImagePage);

        var progression = (await _opds.GetProgressionAsync(_sister, FirstEpub))!;

        Assert.Equal(ImagePage, progression.Page);
        Assert.InRange(progression.Percentage!.Value, 0.6666m, 0.6667m);
    }

    [Fact]
    public async Task WebReaderPercentage_WritesThePercentageTrackAndNotTheImageOne()
    {
        await ReadAsImage(_sister, FirstEpub, ImagePage);
        var ok = await _web.UpdateProgressAsync(_sister, FirstEpub, new DiarSpeicherUpdateProgressInput { Page = 0, Percentage = 42.5f });

        Assert.True(ok);
        var session = await SessionOf(_sister, FirstEpub);
        Assert.Equal(0.425m, session.EndPercentage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
        Assert.Equal(ImagePage, session.RenderedPage);
        Assert.Equal(nameof(ReadingStatus.Reading), session.RenderedStatus);
    }

    [Fact]
    public async Task WebReaderFinishing_FinishesThePercentageTrackOnly()
    {
        await ReadAsImage(_sister, FirstEpub, ImagePage);
        await _web.UpdateProgressAsync(_sister, FirstEpub, new DiarSpeicherUpdateProgressInput { Page = 0, Percentage = 100f });

        var session = await SessionOf(_sister, FirstEpub);
        Assert.Equal(ReadingStatus.Finished, session.Status);
        Assert.Equal(1.0m, session.EndPercentage);
        Assert.Equal(nameof(ReadingStatus.Reading), session.RenderedStatus);
    }

    [Fact]
    public async Task KomgaDelete_ClearsOnlyTheImageTrackOfAnEpub()
    {
        await ReadAsPercentage(_sister, FirstHash, HalfWay);
        await ReadAsImage(_sister, FirstEpub, ImagePage);

        Assert.True(await _komga.DeleteReadProgressAsync(_sister, FirstEpub));

        var session = await SessionOf(_sister, FirstEpub);
        Assert.Null(session.RenderedPage);
        Assert.Null(session.RenderedTotalPages);
        Assert.Null(session.RenderedProfileKey);
        Assert.Null(session.RenderedStatus);
        Assert.Equal(0.5m, session.EndPercentage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
        Assert.Equal(0.5f, (await _koreader.GetProgressAsync(_sister, FirstHash)).Percentage);
        Assert.Null((await _komga.GetBookByIdAsync(_sister, FirstEpub))!.ReadProgress);
        Assert.False(await _komga.DeleteReadProgressAsync(_sister, FirstEpub));
    }

    [Fact]
    public async Task KomgaDelete_RemovesTheWholeSessionOfAnImageOnlyFormat()
    {
        await ReadAsImage(_sister, Cbz, 3);

        Assert.True(await _komga.DeleteReadProgressAsync(_sister, Cbz));

        Assert.Equal(0, await SessionCount(_sister, Cbz));
    }

    [Theory]
    [InlineData(10, null, true, ReadingStatus.Finished, 10)]
    [InlineData(10, false, true, ReadingStatus.Finished, 10)]
    [InlineData(99, null, true, ReadingStatus.Finished, 10)]
    [InlineData(5, null, true, ReadingStatus.Reading, 5)]
    [InlineData(5, false, true, ReadingStatus.Reading, 5)]
    [InlineData(5, true, true, ReadingStatus.Finished, 10)]
    [InlineData(null, true, true, ReadingStatus.Finished, 10)]
    [InlineData(null, false, false, null, null)]
    [InlineData(null, null, false, null, null)]
    public async Task KomgaPatch_FollowsTheKomgaFinishingRules(
        int? page, bool? completed, bool accepted, ReadingStatus? expectedStatus, int? expectedEndPage)
    {
        var result = await _komga.UpdateReadProgressAsync(_brother, Cbz, page, completed);

        Assert.Equal(accepted, result);
        if (expectedStatus is null)
        {
            Assert.Equal(0, await SessionCount(_brother, Cbz));
            return;
        }

        var session = await SessionOf(_brother, Cbz);
        Assert.Equal(expectedStatus, session.Status);
        Assert.Equal(expectedEndPage, session.EndPage);
    }

    [Fact]
    public async Task KomgaPatchCompletedWithoutPage_FinishesTheImageTrackOnTheLastRenderedPage()
    {
        await ReadAsPercentage(_sister, FirstHash, HalfWay);

        Assert.True(await _komga.UpdateReadProgressAsync(_sister, FirstEpub, null, true));

        var session = await SessionOf(_sister, FirstEpub);
        Assert.Equal(EpubPages, session.RenderedPage);
        Assert.Equal(nameof(ReadingStatus.Finished), session.RenderedStatus);
        Assert.Equal(ReadingStatus.Reading, session.Status);
        Assert.Equal(0.5m, session.EndPercentage);
    }

    [Fact]
    public async Task SeriesCounters_AreCountedPerUserAndPerTrack()
    {
        await ReadAsImage(_brother, FirstEpub, EpubPages);
        await ReadAsPercentage(_brother, SecondHash, 1.0f);
        await ReadAsImage(_brother, Cbz, 3);

        var brother = (await _komga.GetSeriesByIdAsync(_brother, SeriesId))!;
        Assert.Equal(3, brother.BooksCount);
        Assert.Equal(1, brother.BooksReadCount);
        Assert.Equal(1, brother.BooksInProgressCount);
        Assert.Equal(1, brother.BooksUnreadCount);

        var sister = (await _komga.GetSeriesByIdAsync(_sister, SeriesId))!;
        Assert.Equal(3, sister.BooksCount);
        Assert.Equal(0, sister.BooksReadCount);
        Assert.Equal(0, sister.BooksInProgressCount);
        Assert.Equal(3, sister.BooksUnreadCount);
    }

    [Fact]
    public async Task MihonTracker_ReadsAndWritesTheImageTrackWithoutTouchingThePercentage()
    {
        await ReadAsPercentage(_brother, SecondHash, HalfWay);

        Assert.True(await _komga.SetTachiyomiProgressAsync(_brother, SeriesId, 2f));

        var afterSet = (await _komga.GetTachiyomiProgressAsync(_brother, SeriesId))!;
        Assert.Equal(3, afterSet.BooksCount);
        Assert.Equal(2, afterSet.BooksReadCount);
        Assert.Equal(0, afterSet.BooksInProgressCount);
        Assert.Equal(1, afterSet.BooksUnreadCount);
        Assert.Equal(2f, afterSet.LastReadContinuousNumberSort);
        Assert.Equal(3f, afterSet.MaxNumberSort);

        var session = await SessionOf(_brother, SecondEpub);
        Assert.Equal(ReadingStatus.Reading, session.Status);
        Assert.Equal(0.5m, session.EndPercentage);

        Assert.True(await _komga.SetTachiyomiProgressAsync(_brother, SeriesId, 1f));

        var afterRewind = (await _komga.GetTachiyomiProgressAsync(_brother, SeriesId))!;
        Assert.Equal(1, afterRewind.BooksReadCount);
        Assert.Equal(2, afterRewind.BooksUnreadCount);
        Assert.Equal(1f, afterRewind.LastReadContinuousNumberSort);

        var rewound = await SessionOf(_brother, SecondEpub);
        Assert.Null(rewound.RenderedStatus);
        Assert.Equal(0.5m, rewound.EndPercentage);
    }

    [Fact]
    public async Task MihonTracker_RejectsAnUnknownSeries()
    {
        Assert.Null(await _komga.GetTachiyomiProgressAsync(_brother, "missing"));
        Assert.False(await _komga.SetTachiyomiProgressAsync(_brother, "missing", 1f));
    }

    [Fact]
    public async Task KeepReading_ListsEachBookOnlyUnderTheTrackItWasReadIn()
    {
        await ReadAsPercentage(_sister, FirstHash, HalfWay);
        await ReadAsImage(_sister, SecondEpub, 5);
        await ReadAsImage(_sister, Cbz, 3);

        var percentage = await _db.Connection.GetKeepReadingSessionsAsync(_sister.Id);
        var image = await _db.Connection.GetKeepReadingSessionsAsync(_sister.Id, imageTrack: true);

        Assert.Equal(new[] { Cbz, FirstEpub }, percentage.Select(s => s.MediaId).Order().ToArray());
        Assert.Equal(new[] { Cbz, SecondEpub }, image.Select(s => s.MediaId).Order().ToArray());
    }

    [Fact]
    public async Task KeepReading_DropsABookFinishedInItsOwnTrackOnly()
    {
        await ReadAsImage(_sister, FirstEpub, EpubPages);
        await ReadAsPercentage(_sister, FirstHash, HalfWay);

        var percentage = await _db.Connection.GetKeepReadingSessionsAsync(_sister.Id);
        var image = await _db.Connection.GetKeepReadingSessionsAsync(_sister.Id, imageTrack: true);

        Assert.Equal(FirstEpub, Assert.Single(percentage).MediaId);
        Assert.Empty(image);
    }

    [Fact]
    public void SchemaUpgrade_AddsTheImageStatusAndBackfillsExistingSessions()
    {
        using var db = new TestSqliteDatabase();
        var conn = db.Connection;
        var now = DateTimeOffset.UtcNow;
        conn.Execute("""INSERT INTO "Users" ("Id", "Username", "CreatedAt") VALUES ('u', 'u', @Now);""", new { Now = now });
        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Size", "Status", "CreatedAt")
            VALUES (@Id, @Id, @Path, 'epub', 1, 'Ready', @Now);
            """, new[]
        {
            new { Id = "a", Path = "/a.epub", Now = now },
            new { Id = "b", Path = "/b.epub", Now = now },
            new { Id = "c", Path = "/c.epub", Now = now }
        });
        conn.Execute("""ALTER TABLE "ReadingSessions" DROP COLUMN "RenderedStatus";""");
        conn.Execute("""
            INSERT INTO "ReadingSessions" ("MediaId", "UserId", "SessionDate", "Status", "RenderedPage", "RenderedTotalPages", "EndPercentage", "CreatedAt")
            VALUES ('a', 'u', '2026-01-01', 'Finished', 100, 400, NULL, @Now),
                   ('b', 'u', '2026-01-01', 'Reading', NULL, NULL, '0.7', @Now),
                   ('c', 'u', '2026-01-01', 'Reading', 50, 100, '0.9', @Now);
            """, new { Now = now });

        conn.InitializeDatabase();

        var rows = conn.Query<ReadingSession>("""SELECT * FROM "ReadingSessions" ORDER BY "MediaId";""").ToDictionary(s => s.MediaId);
        Assert.Equal(nameof(ReadingStatus.Finished), rows["a"].RenderedStatus);
        Assert.Equal(0.25m, rows["a"].EndPercentage);
        Assert.Null(rows["b"].RenderedStatus);
        Assert.Equal(0.7m, rows["b"].EndPercentage);
        Assert.Equal(nameof(ReadingStatus.Reading), rows["c"].RenderedStatus);
        Assert.Equal(0.9m, rows["c"].EndPercentage);

        conn.Execute("""UPDATE "ReadingSessions" SET "RenderedStatus" = 'Reading' WHERE "MediaId" = 'a';""");
        conn.InitializeDatabase();

        Assert.Equal(
            nameof(ReadingStatus.Reading),
            conn.ExecuteScalar<string>("""SELECT "RenderedStatus" FROM "ReadingSessions" WHERE "MediaId" = 'a';"""));
    }
}
