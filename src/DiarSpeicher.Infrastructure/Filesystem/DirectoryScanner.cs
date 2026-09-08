using System.IO;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Infrastructure.Filesystem;

public record ExistingSeriesInfo(string Id, string Path, FileStatus Status);
public record ExistingMediaInfo(string Id, string Path, FileStatus Status, DateTimeOffset? ModifiedAt);

public interface IDirectoryScanner
{
    WalkedLibrary WalkLibrary(string libraryPath, bool isCollectionBased, IReadOnlyList<ExistingSeriesInfo> existingSeries);
    WalkedSeries WalkSeries(string seriesPath, IReadOnlyDictionary<string, long> cachedDirMtimes, IReadOnlyList<ExistingMediaInfo> existingMedia);
}

public class DirectoryScanner : IDirectoryScanner
{
    public WalkedLibrary WalkLibrary(string libraryPath, bool isCollectionBased, IReadOnlyList<ExistingSeriesInfo> existingSeries)
    {
        if (!Directory.Exists(libraryPath))
        {
            return WalkedLibrary.Missing();
        }

        var normalizedLibPath = Path.GetFullPath(libraryPath);
        var validEntries = new List<string>();
        var ignoredEntries = new List<string>();

        // We include the library directory itself (min_depth: 0 in Stump)
        // If library has direct media, the library directory itself is treated as a series
        var dirsToEvaluate = new List<string> { normalizedLibPath };

        if (isCollectionBased)
        {
            // Only top-level subdirectories (depth 1)
            try
            {
                dirsToEvaluate.AddRange(Directory.EnumerateDirectories(normalizedLibPath, "*", SearchOption.TopDirectoryOnly));
            }
            catch (Exception)
            {
                // Inaccessible
            }
        }
        else
        {
            // Bottom-up: all subdirectories recursively
            try
            {
                dirsToEvaluate.AddRange(Directory.EnumerateDirectories(normalizedLibPath, "*", SearchOption.AllDirectories));
            }
            catch (Exception)
            {
                // Inaccessible
            }
        }

        foreach (var dir in dirsToEvaluate)
        {
            var normalizedDir = Path.GetFullPath(dir);
            if (PathUtils.IsHiddenFile(normalizedDir))
            {
                ignoredEntries.Add(normalizedDir);
                continue;
            }

            var isRoot = string.Equals(normalizedDir, normalizedLibPath, StringComparison.OrdinalIgnoreCase);
            var checkDeep = isCollectionBased && !isRoot;

            bool isValid = checkDeep
                ? PathUtils.DirHasMediaDeep(normalizedDir)
                : PathUtils.DirHasMedia(normalizedDir);

            if (isValid)
            {
                validEntries.Add(normalizedDir);
            }
            else
            {
                ignoredEntries.Add(normalizedDir);
            }
        }

        var existingMap = existingSeries.ToDictionary(s => Path.GetFullPath(s.Path), s => s, StringComparer.OrdinalIgnoreCase);

        var missingSeries = existingMap.Values
            .Where(s => !Directory.Exists(s.Path))
            .Select(s => s.Path)
            .ToList();

        var recoveredSeries = existingMap.Values
            .Where(s => s.Status.IsRecoveredIfPresent() && Directory.Exists(s.Path))
            .Select(s => s.Id)
            .ToList();

        // Existing series in ignored entries (e.g. empty directories that previously had books) are still visited
        var existingEmptySeries = ignoredEntries
            .Where(p => existingMap.ContainsKey(p))
            .ToList();

        var candidatesToVisitOrAdd = validEntries
            .Where(p => !missingSeries.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Concat(existingEmptySeries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var seriesToCreate = new List<string>();
        var seriesToVisit = new List<string>();

        foreach (var path in candidatesToVisitOrAdd)
        {
            if (existingMap.ContainsKey(path))
            {
                seriesToVisit.Add(path);
            }
            else
            {
                seriesToCreate.Add(path);
            }
        }

        return new WalkedLibrary
        {
            SeenDirectories = (ulong)(validEntries.Count + ignoredEntries.Count),
            IgnoredDirectories = (ulong)ignoredEntries.Count,
            SeriesToCreate = seriesToCreate,
            RecoveredSeries = recoveredSeries,
            SeriesToVisit = seriesToVisit,
            MissingSeries = missingSeries,
            LibraryIsMissing = false
        };
    }

    public WalkedSeries WalkSeries(
        string seriesPath,
        IReadOnlyDictionary<string, long> cachedDirMtimes,
        IReadOnlyList<ExistingMediaInfo> existingMedia)
    {
        if (!Directory.Exists(seriesPath))
        {
            return WalkedSeries.Missing();
        }

        var normalizedSeriesPath = Path.GetFullPath(seriesPath);
        var observedDirMtimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var validFiles = new List<string>();
        var ignoredFiles = 0UL;

        // Custom directory traversal that short-circuits unchanged subdirectories via mtime
        TraverseSeriesDirectories(
            normalizedSeriesPath,
            normalizedSeriesPath,
            cachedDirMtimes,
            observedDirMtimes,
            validFiles,
            ref ignoredFiles);

        var existingMediaMap = existingMedia.ToDictionary(
            m => Path.GetFullPath(m.Path),
            m => m,
            StringComparer.OrdinalIgnoreCase);

        var mediaToCreate = new List<string>();
        var mediaToVisit = new List<string>();

        foreach (var file in validFiles)
        {
            var normalizedFile = Path.GetFullPath(file);
            if (existingMediaMap.TryGetValue(normalizedFile, out var existing))
            {
                // Check if updated since last scan
                var diskMtime = new DateTimeOffset(File.GetLastWriteTimeUtc(normalizedFile));
                if (existing.ModifiedAt is null || diskMtime > existing.ModifiedAt.Value)
                {
                    mediaToVisit.Add(normalizedFile);
                }
            }
            else
            {
                mediaToCreate.Add(normalizedFile);
            }
        }

        var missingMedia = existingMediaMap.Values
            .Where(m => !File.Exists(m.Path))
            .Select(m => m.Path)
            .ToList();

        var recoveredMedia = existingMediaMap.Values
            .Where(m => m.Status.IsRecoveredIfPresent() && File.Exists(m.Path))
            .Select(m => m.Id)
            .ToList();

        var totalSeenFiles = (ulong)validFiles.Count + ignoredFiles;
        var skippedFiles = totalSeenFiles - (ulong)(mediaToCreate.Count + mediaToVisit.Count);

        return new WalkedSeries
        {
            SeenFiles = totalSeenFiles,
            IgnoredFiles = ignoredFiles,
            SkippedFiles = skippedFiles,
            MediaToCreate = mediaToCreate,
            RecoveredMedia = recoveredMedia,
            MediaToVisit = mediaToVisit,
            MissingMedia = missingMedia,
            SeriesIsMissing = false,
            ObservedDirMtimes = observedDirMtimes
        };
    }

    private static void TraverseSeriesDirectories(
        string currentDir,
        string rootDir,
        IReadOnlyDictionary<string, long> cachedMtimes,
        Dictionary<string, long> observedMtimes,
        List<string> validFiles,
        ref ulong ignoredFiles)
    {
        var normalizedCurrent = Path.GetFullPath(currentDir);
        var dirInfo = new DirectoryInfo(normalizedCurrent);
        if (!dirInfo.Exists)
            return;

        var isRoot = string.Equals(normalizedCurrent, rootDir, StringComparison.OrdinalIgnoreCase);
        var currentMtime = new DateTimeOffset(dirInfo.LastWriteTimeUtc).ToUnixTimeSeconds();

        var didChange = !cachedMtimes.TryGetValue(normalizedCurrent, out var prevMtime) || prevMtime != currentMtime;

        if (didChange)
        {
            observedMtimes[normalizedCurrent] = currentMtime;
        }

        // Stump rule: Never skip the series root itself, but skip subdirectories if unchanged
        if (!isRoot && !didChange)
        {
            return;
        }

        // Process files in this directory
        try
        {
            foreach (var file in dirInfo.EnumerateFiles())
            {
                var fullPath = file.FullName;
                if (PathUtils.IsDefaultIgnored(fullPath))
                {
                    ignoredFiles++;
                }
                else
                {
                    validFiles.Add(fullPath);
                }
            }
        }
        catch (Exception)
        {
            // Inaccessible
        }

        // Process subdirectories
        try
        {
            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                if (PathUtils.IsHiddenFile(subDir.FullName))
                {
                    continue;
                }

                TraverseSeriesDirectories(
                    subDir.FullName,
                    rootDir,
                    cachedMtimes,
                    observedMtimes,
                    validFiles,
                    ref ignoredFiles);
            }
        }
        catch (Exception)
        {
            // Inaccessible
        }
    }
}
