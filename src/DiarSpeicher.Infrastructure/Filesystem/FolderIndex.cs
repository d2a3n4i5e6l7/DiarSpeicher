
namespace DiarSpeicher.Infrastructure.Filesystem;

public sealed record FolderHit(string Path, string Name, string Parent, int Depth);

public sealed record FolderIndexStatus(bool Built, long Folders, DateTimeOffset? UpdatedAt, bool Running);

public interface IFolderIndex
{
    FolderIndexStatus GetStatus();
    IReadOnlyList<FolderHit> Search(string term, int limit);
    bool TryStartRefresh();
}

/// <summary>
/// Lives in its own SQLite file under storage, not in the main database: it is a cache that can
/// be rebuilt from scratch, so it deserves neither a migration nor a place in the backups.
/// Deleting it loses nothing.
///
/// It is only for searching. Browsing folder by folder goes live against the disk, a system call
/// that always tells the truth.
/// </summary>
public sealed partial class FolderIndex : IFolderIndex, IDisposable
{
    private const int MaxDepth = 12;

    private readonly LibraryRootsOptions _roots;
    private readonly ILogger<FolderIndex> _logger;
    private readonly string _databasePath;
    private Task? _refreshTask;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _running;

    public FolderIndex(
        IOptions<LibraryRootsOptions> roots,
        IOptions<Storage.StorageOptions> storage,
        ILogger<FolderIndex> logger)
    {
        _roots = roots.Value;
        _logger = logger;
        _databasePath = Path.Combine(storage.Value.RootPath, "cache", "folders.sqlite");
    }

    public FolderIndexStatus GetStatus()
    {
        if (!File.Exists(_databasePath))
        {
            return new FolderIndexStatus(false, 0, null, _running);
        }

        try
        {
            using var connection = Open(readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), MAX(indexed_at) FROM folders";
            using var reader = command.ExecuteReader();

            if (!reader.Read()) return new FolderIndexStatus(true, 0, null, _running);

            var count = reader.GetInt64(0);
            DateTimeOffset? updated = reader.IsDBNull(1)
                ? null
                : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));

            return new FolderIndexStatus(true, count, updated, _running);
        }
        catch (SqliteException e)
        {
            _logger.LogWarning(e, "No se pudo leer el indice de carpetas");
            return new FolderIndexStatus(false, 0, null, _running);
        }
    }

    /// <summary>
    /// LIKE rather than FTS5 because FTS matches whole words or prefixes: typing "cel" would not
    /// find "Accel", and here people type mid-word.
    ///
    /// The name is stored exactly as on disk; name_lower is a separate column that exists only for
    /// case-insensitive comparison.
    /// </summary>
    public IReadOnlyList<FolderHit> Search(string term, int limit)
    {
        var needle = term.Trim();
        if (needle.Length < 2 || !File.Exists(_databasePath)) return [];

        try
        {
            using var connection = Open(readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT path, name, parent, depth FROM folders
                WHERE name_lower LIKE $needle ESCAPE '\'
                ORDER BY depth, name_lower
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$needle", "%" + Escape(needle.ToLowerInvariant()) + "%");
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

            var hits = new List<FolderHit>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                hits.Add(new FolderHit(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
            }

            return hits;
        }
        catch (SqliteException e)
        {
            _logger.LogWarning(e, "Fallo la busqueda en el indice de carpetas");
            return [];
        }
    }

    /// <summary>
    /// Detached from the request that started it: walking an external disk takes minutes and
    /// must not die when the browser closes the connection.
    /// </summary>
    public bool TryStartRefresh()
    {
        if (!_refreshLock.Wait(0, CancellationToken.None)) return false;

        _running = true;
        _refreshTask = Task.Run(() =>
        {
            try
            {
                Rebuild(_lifetime.Token);
            }
            catch (OperationCanceledException cancelado)
            {
                _logger.LogInformation(cancelado, "Repaso del indice de carpetas cancelado");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Fallo el repaso del indice de carpetas");
            }
            finally
            {
                _running = false;
                _refreshLock.Release();
            }
        }, CancellationToken.None);

        return true;
    }

    private void Rebuild(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        using var connection = Open(readOnly: false);
        EnsureSchema(connection);

        var known = LoadKnownMTimes(connection);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var seen = 0L;
        var written = 0L;

        using var transaction = connection.BeginTransaction();

        using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText =
                """
                INSERT INTO folders (path, name, name_lower, parent, root, depth, mtime, indexed_at)
                VALUES ($path, $name, $lower, $parent, $root, $depth, $mtime, $at)
                ON CONFLICT(path) DO UPDATE SET
                    name = excluded.name, name_lower = excluded.name_lower,
                    mtime = excluded.mtime, indexed_at = excluded.indexed_at
                """;
            var pPath = upsert.Parameters.Add("$path", SqliteType.Text);
            var pName = upsert.Parameters.Add("$name", SqliteType.Text);
            var pLower = upsert.Parameters.Add("$lower", SqliteType.Text);
            var pParent = upsert.Parameters.Add("$parent", SqliteType.Text);
            var pRoot = upsert.Parameters.Add("$root", SqliteType.Text);
            var pDepth = upsert.Parameters.Add("$depth", SqliteType.Integer);
            var pMtime = upsert.Parameters.Add("$mtime", SqliteType.Integer);
            var pAt = upsert.Parameters.Add("$at", SqliteType.Integer);

            using var touch = connection.CreateCommand();
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE folders SET indexed_at = $at WHERE path = $path";
            touch.Parameters.Add("$path", SqliteType.Text);
            touch.Parameters.Add("$at", SqliteType.Integer);

            foreach (var root in _roots.ResolveRoots())
            {
                if (!Directory.Exists(root)) continue;

                foreach (var (directory, depth) in Walk(root, ct))
                {
                    seen++;
                    var mtime = ReadMTime(directory);

                    // The walk still has to be complete: a folder's mtime only changes when
                    // something is added to or removed from it, not when a grandchild changes,
                    // so the subtree cannot be pruned. What this saves is the write, which is
                    // the expensive part when almost nothing changed.
                    if (known.TryGetValue(directory, out var previous) && previous == mtime)
                    {
                        touch.Parameters["$path"].Value = directory;
                        touch.Parameters["$at"].Value = now;
                        touch.ExecuteNonQuery();
                        continue;
                    }

                    pPath.Value = directory;
                    pName.Value = Path.GetFileName(directory);
                    pLower.Value = Path.GetFileName(directory).ToLowerInvariant();
                    pParent.Value = Path.GetDirectoryName(directory) ?? root;
                    pRoot.Value = root;
                    pDepth.Value = depth;
                    pMtime.Value = mtime;
                    pAt.Value = now;
                    upsert.ExecuteNonQuery();
                    written++;
                }
            }
        }

        // Whatever was not seen again in this pass is no longer on disk. Comparing by the pass
        // marker avoids keeping a list of paths in memory.
        using (var prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText = "DELETE FROM folders WHERE indexed_at < $at";
            prune.Parameters.AddWithValue("$at", now);
            prune.ExecuteNonQuery();
        }

        transaction.Commit();
        _logger.LogInformation(
            "Indice de carpetas repasado: {Seen} carpetas, {Written} con cambios", seen, written);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS folders (
                path TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                name_lower TEXT NOT NULL,
                parent TEXT NOT NULL,
                root TEXT NOT NULL,
                depth INTEGER NOT NULL,
                mtime INTEGER NOT NULL,
                indexed_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_folders_name ON folders (name_lower);
            """;
        command.ExecuteNonQuery();
    }
}
