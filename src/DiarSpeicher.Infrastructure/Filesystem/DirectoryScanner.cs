using System.IO;

namespace DiarSpeicher.Infrastructure.Filesystem;

public record ExistingSeriesInfo(string Id, string Path, FileStatus Status);
public record ExistingMediaInfo(string Id, string Path, FileStatus Status, DateTimeOffset? ModifiedAt);

public interface IDirectoryScanner
{
    WalkedLibrary WalkLibrary(string libraryPath, bool isCollectionBased, IReadOnlyList<ExistingSeriesInfo> existingSeries);
    WalkedSeries WalkSeries(
        string seriesPath,
        IReadOnlyDictionary<string, long> cachedDirMtimes,
        IReadOnlyList<ExistingMediaInfo> existingMedia,
        IReadOnlyCollection<string>? otherSeriesPaths = null);
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
        var dirsToEvaluate = DiscoverDirectories(normalizedLibPath, isCollectionBased);
        var (validEntries, ignoredEntries) = ClassifyDirectories(dirsToEvaluate, normalizedLibPath, isCollectionBased);

        // Las rutas se comparan byte a byte, igual que el sistema de ficheros: en Linux
        // "parte 2" y "Parte 2" son dos carpetas distintas y las dos pueden estar indexadas.
        // Y se monta con el indexador, no con ToDictionary: Series.Path no es unico, y una
        // clave repetida tumbaba el escaneo entero con ArgumentException.
        var existingMap = new Dictionary<string, ExistingSeriesInfo>(StringComparer.Ordinal);
        foreach (var existing in existingSeries)
        {
            existingMap[Path.GetFullPath(existing.Path)] = existing;
        }

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
            .Where(p => existingMap.ContainsKey(p));

        var missingSeriesSet = new HashSet<string>(missingSeries, StringComparer.Ordinal);

        var candidatesToVisitOrAdd = validEntries
            .Where(p => !missingSeriesSet.Contains(p))
            .Concat(existingEmptySeries)
            .Distinct(StringComparer.Ordinal);

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
        IReadOnlyList<ExistingMediaInfo> existingMedia,
        IReadOnlyCollection<string>? otherSeriesPaths = null)
    {
        if (!Directory.Exists(seriesPath))
        {
            return WalkedSeries.Missing();
        }

        var normalizedSeriesPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(seriesPath));
        var observedDirMtimes = new Dictionary<string, long>(StringComparer.Ordinal);

        var validFiles = new List<string>();
        var ignoredFiles = 0UL;

        // Una carpeta que ya es serie por si misma no aporta tomos a la de arriba. Sin este
        // corte, un libro suelto en la raiz la convierte en serie y el recorrido se lleva
        // ademas cuanto cuelga de las series hijas: el mismo fichero acaba con dos filas
        // y dos dueños, y los contadores de la biblioteca suman de mas.
        var boundaries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var other in otherSeriesPaths ?? [])
        {
            var normalizedOther = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other));
            if (!string.Equals(normalizedOther, normalizedSeriesPath, StringComparison.Ordinal))
            {
                boundaries.Add(normalizedOther);
            }
        }

        // Custom directory traversal that short-circuits unchanged subdirectories via mtime
        TraverseSeriesDirectories(
            normalizedSeriesPath,
            normalizedSeriesPath,
            cachedDirMtimes,
            observedDirMtimes,
            validFiles,
            boundaries,
            ref ignoredFiles);

        var existingMediaMap = new Dictionary<string, ExistingMediaInfo>(StringComparer.Ordinal);
        foreach (var existing in existingMedia)
        {
            existingMediaMap[Path.GetFullPath(existing.Path)] = existing;
        }

        var mediaToCreate = new List<string>();
        var mediaToVisit = new List<string>();

        foreach (var file in validFiles)
        {
            var normalizedFile = Path.GetFullPath(file);
            if (existingMediaMap.TryGetValue(normalizedFile, out var existing))
            {
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
        IReadOnlySet<string> boundaries,
        ref ulong ignoredFiles)
    {
        var normalizedCurrent = Path.GetFullPath(currentDir);
        var dirInfo = new DirectoryInfo(normalizedCurrent);
        if (!dirInfo.Exists)
            return;

        var isRoot = string.Equals(normalizedCurrent, rootDir, StringComparison.Ordinal);
        var currentMtime = new DateTimeOffset(dirInfo.LastWriteTimeUtc).ToUnixTimeSeconds();

        var didChange = !cachedMtimes.TryGetValue(normalizedCurrent, out var prevMtime) || prevMtime != currentMtime;

        if (didChange)
        {
            observedMtimes[normalizedCurrent] = currentMtime;
        }

        if (!isRoot && !didChange)
        {
            return;
        }

        try
        {
            foreach (var fullPath in dirInfo.EnumerateFiles().Select(f => f.FullName))
            {
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
            // Una carpeta sin permiso o borrada a mitad del recorrido se queda sin
            // indexar; tumbar el escaneo entero por ella seria peor.
        }

        try
        {
            foreach (var subDirFullName in dirInfo.EnumerateDirectories().Select(d => d.FullName))
            {
                if (PathUtils.IsHiddenFile(subDirFullName))
                {
                    continue;
                }

                if (boundaries.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(subDirFullName))))
                {
                    continue;
                }

                TraverseSeriesDirectories(
                    subDirFullName,
                    rootDir,
                    cachedMtimes,
                    observedMtimes,
                    validFiles,
                    boundaries,
                    ref ignoredFiles);
            }
        }
        catch (Exception)
        {
            // Igual que arriba: lo que no se pueda leer se omite y el resto sigue.
        }
    }

    private static List<string> DiscoverDirectories(string normalizedLibPath, bool isCollectionBased)
    {
        var dirsToEvaluate = new List<string> { normalizedLibPath };
        var searchOption = isCollectionBased ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;

        try
        {
            dirsToEvaluate.AddRange(Directory.EnumerateDirectories(normalizedLibPath, "*", searchOption));
        }
        catch (Exception)
        {
            // Se devuelve al menos la raiz: una biblioteca ilegible se marca Missing
            // mas adelante, aqui no se decide nada.
        }

        return dirsToEvaluate;
    }

    private static (List<string> ValidEntries, List<string> IgnoredEntries) ClassifyDirectories(
        List<string> dirsToEvaluate,
        string normalizedLibPath,
        bool isCollectionBased)
    {
        var validEntries = new List<string>();
        var ignoredEntries = new List<string>();

        foreach (var dir in dirsToEvaluate)
        {
            var normalizedDir = Path.GetFullPath(dir);
            if (PathUtils.IsHiddenFile(normalizedDir))
            {
                ignoredEntries.Add(normalizedDir);
                continue;
            }

            var isRoot = string.Equals(normalizedDir, normalizedLibPath, StringComparison.Ordinal);
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

        return (validEntries, ignoredEntries);
    }
}
