
namespace DiarSpeicher.Infrastructure.Filesystem;

public sealed partial class FolderIndex
{
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
    /// Iterative and depth-capped: a symbolic link pointing upwards turns recursion into an
    /// infinite loop.
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

        var finished = _refreshTask?.Wait(TimeSpan.FromSeconds(5), CancellationToken.None) ?? true;

        _lifetime.Dispose();

        if (finished)
        {
            _refreshLock.Dispose();
            return;
        }

        _logger.LogWarning("El repaso del indice de carpetas seguia corriendo al apagar; su cerrojo se deja sin liberar");
    }
}
