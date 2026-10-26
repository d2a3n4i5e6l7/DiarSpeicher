using System.Text.Json;
using DiarSpeicher.Infrastructure.Serialization;

namespace DiarSpeicher.Infrastructure.Filesystem;

public sealed record TrashEntry
{
    public string Id { get; init; } = string.Empty;
    public string OriginalPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public int FileCount { get; init; }
    public long Bytes { get; init; }
    public DateTimeOffset DeletedAt { get; init; }
}

public sealed record DeletionScope(string Path, string Name, int FileCount, long Bytes, IReadOnlyList<DeletionScope> Children);

/// <summary>
/// "Does not exist" and "not allowed" must travel separately: they are different answers for
/// the user, and removing from the index something whose file is gone is legitimate.
/// </summary>
public enum TrashOutcome
{
    Moved,
    NotFound,
    NotAllowed,
    Failed
}

public interface ITrashService
{
    DeletionScope? Inspect(string path);
    TrashOutcome TryMoveToTrash(string path, out TrashEntry? entry);
    IReadOnlyList<TrashEntry> List();
    bool Restore(string id);

    bool PurgeNow(string id);
    void PurgeExpired();
}

public sealed class NullTrashService : ITrashService
{
    public DeletionScope? Inspect(string path) => null;
    public TrashOutcome TryMoveToTrash(string path, out TrashEntry? entry) { entry = null; return TrashOutcome.NotAllowed; }
    public IReadOnlyList<TrashEntry> List() => [];
    public bool Restore(string id) => false;
    public bool PurgeNow(string id) => false;
    public void PurgeExpired() { }
}

/// <summary>
/// Nothing is really deleted on request: it is moved inside the declared root itself, into a
/// folder starting with a dot so the scanner ignores it. It has to be there and not in storage
/// because <c>File.Move</c> across file systems degrades to copy and delete, and moving a whole
/// library would mean copying it whole.
///
/// The index of each item is a JSON next to it, not a table: if the process dies halfway, the
/// trash still explains itself and can be emptied by hand.
/// </summary>
public sealed class TrashService : ITrashService
{
    public const string FolderName = ".diarspeicher-trash";
    private const string MetaName = "entry.json";
    private const string PayloadName = "payload";

    private readonly LibraryRootsOptions _roots;
    private readonly TrashOptions _options;
    private readonly ILogger<TrashService> _logger;
    private readonly Lock _gate = new();

    public TrashService(
        IOptions<LibraryRootsOptions> roots,
        IOptions<TrashOptions> options,
        ILogger<TrashService> logger)
    {
        _roots = roots.Value;
        _options = options.Value;
        _logger = logger;
    }

    public DeletionScope? Inspect(string path)
    {
        if (!CanTouch(path, out var resolved)) return null;

        if (File.Exists(resolved))
        {
            var info = new FileInfo(resolved);

            return new DeletionScope(resolved, info.Name, 1, info.Length, []);
        }

        if (!Directory.Exists(resolved)) return null;

        var children = new List<DeletionScope>();
        try
        {
            foreach (var child in Directory.EnumerateDirectories(resolved).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(child).StartsWith('.')) continue;

                var (files, bytes) = Measure(child);
                children.Add(new DeletionScope(child, Path.GetFileName(child), files, bytes, []));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(e, "No se pudo inspeccionar {Path}", resolved);
        }

        var (totalFiles, totalBytes) = Measure(resolved);

        return new DeletionScope(resolved, Path.GetFileName(resolved), totalFiles, totalBytes, children);
    }

    public TrashOutcome TryMoveToTrash(string path, out TrashEntry? entry)
    {
        entry = null;
        if (!CanTouch(path, out var resolved)) return TrashOutcome.NotAllowed;

        var isDirectory = Directory.Exists(resolved);
        if (!isDirectory && !File.Exists(resolved)) return TrashOutcome.NotFound;

        var root = RootOf(resolved);
        if (root == null) return TrashOutcome.NotAllowed;

        var (files, bytes) = isDirectory ? Measure(resolved) : (1, new FileInfo(resolved).Length);

        var moved = new TrashEntry
        {
            Id = Ulid.NewUlid().ToString(),
            OriginalPath = resolved,
            Name = Path.GetFileName(resolved),
            IsDirectory = isDirectory,
            FileCount = files,
            Bytes = bytes,
            DeletedAt = DateTimeOffset.UtcNow
        };

        var slot = Path.Combine(root, FolderName, moved.Id);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(slot);
                var destination = Path.Combine(slot, PayloadName);

                // Metadata first. A power cut between the move and the JSON would leave the
                // content in the trash without knowing where it came from: impossible to
                // restore. The other way round only leaves an empty slot the purge cleans.
                File.WriteAllText(Path.Combine(slot, MetaName), JsonSerializer.Serialize(moved, InfrastructureJsonContext.Default.TrashEntry));

                // Rename within the same file system: it is atomic, so the "half moved" state
                // does not exist.
                if (isDirectory)
                {
                    Directory.Move(resolved, destination);
                }
                else
                {
                    File.Move(resolved, destination);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(e, "No se pudo mover a la papelera {Path}", resolved);
                TryRemoveDirectory(slot);

                return TrashOutcome.Failed;
            }
        }

        _logger.LogInformation("A la papelera: {Path} ({Files} ficheros)", resolved, files);
        entry = moved;

        return TrashOutcome.Moved;
    }

    public IReadOnlyList<TrashEntry> List()
    {
        var entries = new List<TrashEntry>();

        foreach (var slot in EnumerateSlots())
        {
            if (ReadEntry(slot) is not { } entry) continue;

            var payload = Path.Combine(slot, PayloadName);
            if (Directory.Exists(payload) || File.Exists(payload)) entries.Add(entry);
        }

        return entries.OrderByDescending(e => e.DeletedAt).ToList();
    }

    private bool RestoreEntry(string slot, TrashEntry entry)
    {
        var payload = Path.Combine(slot, PayloadName);

        // Slot without content: the process died between writing the JSON and moving. There is
        // nothing to restore, and the purge will take it.
        if (!Directory.Exists(payload) && !File.Exists(payload)) return false;

        // If someone created something with that name in the meantime, it is not overwritten.
        if (Directory.Exists(entry.OriginalPath) || File.Exists(entry.OriginalPath)) return false;

        try
        {
            var parent = Path.GetDirectoryName(entry.OriginalPath);
            if (parent != null) Directory.CreateDirectory(parent);

            if (entry.IsDirectory)
            {
                Directory.Move(payload, entry.OriginalPath);
            }
            else
            {
                File.Move(payload, entry.OriginalPath);
            }

            TryRemoveDirectory(slot);
            _logger.LogInformation("Restaurado {Path}", entry.OriginalPath);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "No se pudo restaurar {Path}", entry.OriginalPath);

            return false;
        }
    }

    public bool Restore(string id)
    {
        lock (_gate)
        {
            foreach (var slot in EnumerateSlots())
            {
                var entry = ReadEntry(slot);
                if (entry == null || entry.Id != id) continue;

                return RestoreEntry(slot, entry);
            }
        }

        return false;
    }

    public bool PurgeNow(string id)
    {
        lock (_gate)
        {
            foreach (var slot in EnumerateSlots())
            {
                if (ReadEntry(slot)?.Id != id) continue;

                TryRemoveDirectory(slot);
                _logger.LogInformation("Papelera vaciada a mano: {Id}", id);

                return true;
            }
        }

        return false;
    }

    public void PurgeExpired()
    {
        var deadline = DateTimeOffset.UtcNow - _options.Retention;

        lock (_gate)
        {
            foreach (var slot in EnumerateSlots())
            {
                var entry = ReadEntry(slot);
                // A slot without readable metadata is discarded by folder date: otherwise it
                // would take up disk forever.
                var deletedAt = entry?.DeletedAt ?? new DateTimeOffset(Directory.GetCreationTimeUtc(slot));
                if (deletedAt > deadline) continue;

                TryRemoveDirectory(slot);
                _logger.LogInformation("Papelera purgada: {Path}", entry?.OriginalPath ?? slot);
            }
        }
    }

    private IEnumerable<string> EnumerateSlots()
    {
        foreach (var root in _roots.ResolveRoots())
        {
            var trash = Path.Combine(root, FolderName);
            if (!Directory.Exists(trash)) continue;

            string[] slots;
            try
            {
                slots = Directory.GetDirectories(trash);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var slot in slots) yield return slot;
        }
    }

    private static TrashEntry? ReadEntry(string slot)
    {
        try
        {
            var meta = Path.Combine(slot, MetaName);

            return File.Exists(meta) ? JsonSerializer.Deserialize(File.ReadAllText(meta), InfrastructureJsonContext.Default.TrashEntry) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Besides being inside a root: never the root itself nor anything inside the trash, the
    /// two ways of turning a deletion into a catastrophe.
    /// </summary>
    private bool CanTouch(string? path, out string resolved)
    {
        resolved = string.Empty;
        if (!_roots.TryResolve(path, out var candidate)) return false;

        if (_roots.ResolveRoots().Contains(candidate, StringComparer.Ordinal)) return false;

        if (candidate.Split(Path.DirectorySeparatorChar).Contains(FolderName, StringComparer.Ordinal)) return false;

        resolved = candidate;

        return true;
    }

    private string? RootOf(string path) =>
        _roots.ResolveRoots().FirstOrDefault(root =>
            path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static (int Files, long Bytes) Measure(string directory)
    {
        var files = 0;
        var bytes = 0L;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                files++;
                try
                {
                    bytes += new FileInfo(file).Length;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // The file is counted even if it cannot be measured.
                }
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            // Whatever cannot be walked is not counted; the warning will say less, never more.
        }

        return (files, bytes);
    }

    private void TryRemoveDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(e, "No se pudo limpiar {Path}", path);
        }
    }
}

public class TrashOptions
{
    public const string SectionName = "Trash";

    public int RetentionMinutes { get; set; } = 60;

    public TimeSpan Retention => TimeSpan.FromMinutes(Math.Max(1, RetentionMinutes));
}
