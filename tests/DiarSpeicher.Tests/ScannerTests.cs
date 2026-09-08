using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _tempRootDir;

    public ScannerTests()
    {
        _tempRootDir = Path.Combine(Path.GetTempPath(), "diarspeicher_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRootDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRootDir))
            {
                Directory.Delete(_tempRootDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in temp dir
        }
        GC.SuppressFinalize(this);
    }

    private DiarSpeicherDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        var context = new DiarSpeicherDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void DirectoryScanner_WalkLibrary_DetectsNewSeries_AndIgnoresHiddenAndEmpty()
    {
        // Arrange
        var libDir = Path.Combine(_tempRootDir, "MangaLib");
        Directory.CreateDirectory(libDir);

        var seriesA = Path.Combine(libDir, "Berserk");
        Directory.CreateDirectory(seriesA);
        File.WriteAllText(Path.Combine(seriesA, "ch01.cbz"), "dummy cbz content");

        var hiddenSeries = Path.Combine(libDir, ".HiddenManga");
        Directory.CreateDirectory(hiddenSeries);
        File.WriteAllText(Path.Combine(hiddenSeries, "ch01.cbz"), "dummy content");

        var macosxDir = Path.Combine(libDir, "__MACOSX");
        Directory.CreateDirectory(macosxDir);
        File.WriteAllText(Path.Combine(macosxDir, "ch01.cbz"), "dummy mac content");

        var emptySeries = Path.Combine(libDir, "EmptySeries");
        Directory.CreateDirectory(emptySeries);

        var scanner = new DirectoryScanner();

        // Act
        var result = scanner.WalkLibrary(libDir, isCollectionBased: false, existingSeries: []);

        // Assert
        Assert.False(result.LibraryIsMissing);
        Assert.Single(result.SeriesToCreate);
        Assert.Equal(Path.GetFullPath(seriesA), Path.GetFullPath(result.SeriesToCreate[0]));
    }

    [Fact]
    public void DirectoryScanner_WalkSeries_ShortCircuitsUnchangedSubdirectories()
    {
        // Arrange
        var seriesDir = Path.Combine(_tempRootDir, "SoloLeveling");
        Directory.CreateDirectory(seriesDir);

        var vol1 = Path.Combine(seriesDir, "Vol01");
        Directory.CreateDirectory(vol1);
        var file1 = Path.Combine(vol1, "c01.cbz");
        File.WriteAllText(file1, "dummy");

        var vol1Mtime = new DateTimeOffset(Directory.GetLastWriteTimeUtc(vol1)).ToUnixTimeSeconds();
        var cachedMtimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(vol1)] = vol1Mtime
        };

        var scanner = new DirectoryScanner();

        // Act: vol1 mtime matches cache, so it should short-circuit and not traverse inside vol1
        var result = scanner.WalkSeries(seriesDir, cachedMtimes, existingMedia: []);

        // Assert: because vol1 was skipped, file1 was not picked up as media to create
        Assert.Empty(result.MediaToCreate);
        Assert.False(result.ObservedDirMtimes.ContainsKey(Path.GetFullPath(vol1)));
    }

    [Fact]
    public async Task LibraryScannerService_FullCycle_Discover_Missing_And_Recover()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();

        var libDir = Path.Combine(_tempRootDir, "BooksLibrary");
        Directory.CreateDirectory(libDir);

        var seriesDir = Path.Combine(libDir, "OnePiece");
        Directory.CreateDirectory(seriesDir);

        var bookPath = Path.Combine(seriesDir, "vol01.cbz");
        await File.WriteAllTextAsync(bookPath, "cbz dummy data");

        var libraryId = Guid.NewGuid().ToString();
        var library = new Library
        {
            Id = libraryId,
            Name = "Books",
            Path = libDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig
            {
                LibraryPattern = LibraryPattern.SeriesBased
            }
        };

        dbContext.Libraries.Add(library);
        await dbContext.SaveChangesAsync();

        var scanner = new DirectoryScanner();
        var scannerService = new LibraryScannerService(dbContext, scanner, NullLogger<LibraryScannerService>.Instance);

        // Act 1: Initial scan (Discovery)
        var report1 = await scannerService.ScanLibraryAsync(libraryId);

        // Assert 1
        Assert.True(report1.Success);
        Assert.Equal(1UL, report1.CreatedSeries);
        Assert.Equal(1UL, report1.CreatedMedia);

        var createdMedia = await dbContext.Media.FirstOrDefaultAsync(m => m.Path == bookPath);
        Assert.NotNull(createdMedia);
        Assert.Equal(FileStatus.Ready, createdMedia.Status);
        Assert.Equal("vol01", createdMedia.Name);
        Assert.Equal("cbz", createdMedia.Extension);

        // Verify ScannedDirectories has recorded mtimes
        var scannedDirs = await dbContext.ScannedDirectories.ToListAsync();
        Assert.NotEmpty(scannedDirs);

        // Act 2: Remove file from disk and rescan (Missing)
        File.Delete(bookPath);
        var report2 = await scannerService.ScanLibraryAsync(libraryId);

        // Assert 2
        Assert.True(report2.Success);
        var missingMedia = await dbContext.Media.FirstOrDefaultAsync(m => m.Path == bookPath);
        Assert.NotNull(missingMedia);
        Assert.Equal(FileStatus.Missing, missingMedia.Status);

        // Act 3: Restore file on disk and rescan (Recovered)
        await File.WriteAllTextAsync(bookPath, "restored cbz data");
        var report3 = await scannerService.ScanLibraryAsync(libraryId);

        // Assert 3
        Assert.True(report3.Success);
        var restoredMedia = await dbContext.Media.FirstOrDefaultAsync(m => m.Path == bookPath);
        Assert.NotNull(restoredMedia);
        Assert.Equal(FileStatus.Ready, restoredMedia.Status);
    }
}
