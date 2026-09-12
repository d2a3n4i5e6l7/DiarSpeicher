using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// Indice de nombres de carpeta para buscarlas sin recorrer el disco en cada tecla.
/// <para>
/// Vive en su propio fichero SQLite bajo el almacenamiento, no en la base principal: es
/// una cache reconstruible desde cero, asi que no merece ni una migracion ni entrar en
/// las copias de seguridad. Borrarla no pierde nada.
/// </para>
/// <para>
/// Solo sirve para buscar. Navegar carpeta a carpeta va en vivo contra el disco, que es
/// una llamada al sistema y siempre dice la verdad.
/// </para>
/// </summary>
public sealed class FolderIndex : IFolderIndex, IDisposable
{
    private const int MaxDepth = 12;

    private readonly LibraryRootsOptions _roots;
    private readonly ILogger<FolderIndex> _logger;
    private readonly string _databasePath;
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
    /// Busca por trozo de nombre. LIKE y no FTS5 porque FTS casa palabras enteras o prefijos:
    /// escribir "cel" no encontraria "Accel". Aqui se teclea a mitad de palabra.
    /// <para>
    /// El nombre se guarda tal cual esta en disco; name_lower es una columna aparte y solo
    /// existe para comparar sin mayusculas.
    /// </para>
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
    /// Arranca el repaso y vuelve enseguida; false si ya habia uno en marcha. El progreso se
    /// consulta en <see cref="GetStatus"/>.
    /// <para>
    /// Desligado de la peticion que lo lanzo: recorrer un disco externo dura minutos y no
    /// puede morir cuando el navegador cierre la conexion.
    /// </para>
    /// </summary>
    public bool TryStartRefresh()
    {
        if (!_refreshLock.Wait(0)) return false;

        _running = true;
        _ = Task.Run(() =>
        {
            try
            {
                Rebuild(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Repaso del indice de carpetas cancelado");
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

                    // El recorrido hay que hacerlo entero igual: el mtime de una carpeta solo
                    // cambia cuando se anade o quita algo dentro de ella, no cuando cambia un
                    // nieto, asi que no se puede podar el subarbol. Lo que ahorra es la
                    // escritura, que es la parte cara cuando casi nada ha cambiado.
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

        // Lo que no se ha vuelto a ver en este repaso ya no esta en disco. Comparar por la
        // marca del repaso evita tener que llevar una lista de rutas en memoria.
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

    private static Dictionary<string, long> LoadKnownMTimes(SqliteConnection connection)
    {
        var known = new Dictionary<string, long>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, mtime FROM folders";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            known[reader.GetString(0)] = reader.GetInt64(1);
        }

        return known;
    }

    /// <summary>
    /// Recorrido iterativo y con tope de profundidad: un enlace simbolico que apunte hacia
    /// arriba convierte la recursion en un bucle infinito.
    /// </summary>
    private static IEnumerable<(string Path, int Depth)> Walk(string root, CancellationToken ct)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();

            if (depth > 0) yield return (current, depth);
            if (depth >= MaxDepth) continue;

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (Path.GetFileName(child).StartsWith('.')) continue;
                pending.Push((child, depth + 1));
            }
        }
    }

    private static long ReadMTime(string directory)
    {
        try
        {
            return new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory)).ToUnixTimeSeconds();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return 0;
        }
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

    private SqliteConnection Open(bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        connection.Open();
        return connection;
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _refreshLock.Dispose();
    }
}
