using System.IO.Compression;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

/// <summary>
/// The jobProgress subscription of block B5 requires the scanner to emit progress during the
/// run, not only the final report.
/// </summary>
public sealed class ScanProgressTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _libraryDir = Directory.CreateTempSubdirectory("diar-progress-tests-").FullName;

    public ScanProgressTests()
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
        if (Directory.Exists(_libraryDir)) Directory.Delete(_libraryDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AScan_EmitsIncrementalProgress_NotJustTheFinalReport()
    {
        var libraryId = await SeedLibraryAsync(seriesCount: 3);
        var recorder = new RecordingPublisher();

        var report = await NewScanner(recorder).ScanLibraryAsync(libraryId);

        Assert.True(report.Success);

        var phases = recorder.Events.Select(e => e.Phase).ToList();
        Assert.Contains(ScanPhase.Started, phases);
        Assert.Contains(ScanPhase.WalkingLibrary, phases);
        Assert.Contains(ScanPhase.Completed, phases);

        var perSeries = recorder.Events.Where(e => e.Phase == ScanPhase.ProcessingSeries).ToList();
        Assert.Equal(3, perSeries.Count);
        Assert.All(perSeries, e => Assert.Equal(3, e.TotalSeries));
        Assert.Equal([0, 1, 2], perSeries.Select(e => e.CompletedSeries));
        Assert.All(perSeries, e => Assert.Equal(libraryId, e.JobId));
    }

    [Fact]
    public async Task ProgressReportsAGrowingPercentage()
    {
        var libraryId = await SeedLibraryAsync(seriesCount: 4);
        var recorder = new RecordingPublisher();

        await NewScanner(recorder).ScanLibraryAsync(libraryId);

        var percentages = recorder.Events
            .Where(e => e.Phase == ScanPhase.ProcessingSeries)
            .Select(e => e.Percentage)
            .ToList();

        Assert.Equal([0d, 25d, 50d, 75d], percentages);
    }

    [Fact]
    public async Task AMissingLibrary_ReportsFailure()
    {
        var recorder = new RecordingPublisher();

        var report = await NewScanner(recorder).ScanLibraryAsync("no-existe");

        Assert.False(report.Success);
        Assert.Contains(recorder.Events, e => e.Phase == ScanPhase.Failed);
    }

    [Fact]
    public async Task AScanWithoutAPublisherStillWorks()
    {
        var libraryId = await SeedLibraryAsync(seriesCount: 1);

        var report = await NewScanner(progressPublisher: null).ScanLibraryAsync(libraryId);

        Assert.True(report.Success);
    }

    private LibraryScannerService NewScanner(IScanProgressPublisher? progressPublisher)
    {
        var composite = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });

        return new LibraryScannerService(
            new DiarSpeicherDbContext(_options),
            new DirectoryScanner(),
            composite,
            new ThumbnailService(composite, NullLogger<ThumbnailService>.Instance),
            new ArchiveConversionService(NullLogger<ArchiveConversionService>.Instance),
            NullLogger<LibraryScannerService>.Instance,
            storageOptions: null,
            progressPublisher: progressPublisher);
    }

    private async Task<string> SeedLibraryAsync(int seriesCount)
    {
        for (var i = 1; i <= seriesCount; i++)
        {
            var seriesDir = Path.Combine(_libraryDir, $"Serie{i:D2}");
            Directory.CreateDirectory(seriesDir);
            await File.WriteAllBytesAsync(Path.Combine(seriesDir, "vol1.cbz"), SampleCbz());
        }

        await using var db = new DiarSpeicherDbContext(_options);
        var library = new Library
        {
            Name = "Comics",
            Path = _libraryDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig()
        };

        db.Libraries.Add(library);
        await db.SaveChangesAsync();

        return library.Id;
    }

    private static byte[] SampleCbz()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("001.png");
            using var stream = entry.Open();
            stream.Write(PngBuilder.Solid(4, 4));
        }

        return buffer.ToArray();
    }

    private sealed class RecordingPublisher : IScanProgressPublisher
    {
        public List<ScanProgressEvent> Events { get; } = [];

        public ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default)
        {
            Events.Add(progress);
            return ValueTask.CompletedTask;
        }
    }
}
