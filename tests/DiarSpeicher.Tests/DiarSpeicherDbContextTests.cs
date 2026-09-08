using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class DiarSpeicherDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    public DiarSpeicherDbContextTests()
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
    }

    [Fact]
    public async Task Can_Insert_And_Retrieve_Library_With_Series_And_Media()
    {
        using (var context = new DiarSpeicherDbContext(_options))
        {
            var config = new LibraryConfig
            {
                LibraryType = LibraryType.Comic,
                LibraryPattern = LibraryPattern.SeriesBased
            };
            context.LibraryConfigs.Add(config);
            await context.SaveChangesAsync();

            var library = new Library
            {
                Name = "Comics Library",
                Path = "/data/comics",
                ConfigId = config.Id
            };
            context.Libraries.Add(library);
            await context.SaveChangesAsync();

            var series = new Series
            {
                Name = "Batman (2016)",
                Path = "/data/comics/Batman",
                LibraryId = library.Id,
                Metadata = new SeriesMetadata
                {
                    Publisher = "DC Comics",
                    AgeRating = 12
                }
            };
            context.Series.Add(series);
            await context.SaveChangesAsync();

            var media = new Media
            {
                Name = "Batman 001.cbz",
                Path = "/data/comics/Batman/Batman 001.cbz",
                Extension = "cbz",
                Pages = 32,
                Size = 1024 * 1024 * 25,
                SeriesId = series.Id,
                Metadata = new MediaMetadata
                {
                    Title = "I Am Gotham - Part 1",
                    Number = 1.0m,
                    AgeRating = 12
                }
            };
            context.Media.Add(media);
            await context.SaveChangesAsync();
        }

        using (var context = new DiarSpeicherDbContext(_options))
        {
            var savedMedia = await context.Media
                .Include(m => m.Series)
                    .ThenInclude(s => s!.Library)
                .Include(m => m.Metadata)
                .FirstOrDefaultAsync(m => m.Name == "Batman 001.cbz");

            Assert.NotNull(savedMedia);
            Assert.Equal("cbz", savedMedia.Extension);
            Assert.NotNull(savedMedia.Series);
            Assert.Equal("Batman (2016)", savedMedia.Series.Name);
            Assert.NotNull(savedMedia.Series.Library);
            Assert.Equal("Comics Library", savedMedia.Series.Library.Name);
            Assert.NotNull(savedMedia.Metadata);
            Assert.Equal(12, savedMedia.Metadata.AgeRating);
            Assert.Equal(1.0m, savedMedia.Metadata.Number);
        }
    }

    [Fact]
    public async Task Can_Track_Reading_Sessions_With_ReadingStatus()
    {
        string mediaId;
        using (var context = new DiarSpeicherDbContext(_options))
        {
            var config = new LibraryConfig { LibraryType = LibraryType.Manga };
            context.LibraryConfigs.Add(config);
            await context.SaveChangesAsync();

            var library = new Library { Name = "Manga", Path = "/data/manga", ConfigId = config.Id };
            context.Libraries.Add(library);
            await context.SaveChangesAsync();

            var series = new Series { Name = "One Piece", Path = "/data/manga/op", LibraryId = library.Id };
            context.Series.Add(series);
            await context.SaveChangesAsync();

            var media = new Media { Name = "Chapter 1.cbz", Path = "/data/manga/op/01.cbz", Extension = "cbz", SeriesId = series.Id };
            context.Media.Add(media);
            await context.SaveChangesAsync();

            mediaId = media.Id;
        }

        using (var context = new DiarSpeicherDbContext(_options))
        {
            var user = new User { Id = "user_reader_1", Username = "reader1" };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = mediaId,
                StartPage = 1,
                EndPage = 15,
                StartPercentage = 0.0m,
                EndPercentage = 0.5m,
                Status = ReadingStatus.Reading,
                ReadthroughNumber = 1
            };

            context.ReadingSessions.Add(session);
            await context.SaveChangesAsync();
        }

        using (var context = new DiarSpeicherDbContext(_options))
        {
            var session = await context.ReadingSessions
                .FirstOrDefaultAsync(s => s.UserId == "user_reader_1");

            Assert.NotNull(session);
            Assert.Equal(15, session.EndPage);
            Assert.Equal(ReadingStatus.Reading, session.Status);
            Assert.False(session.IsComplete());
            Assert.False(session.IsFinalized());
        }
    }
}
