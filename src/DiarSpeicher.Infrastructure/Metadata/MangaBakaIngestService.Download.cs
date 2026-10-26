using DiarSpeicher.Core.Domain.MangaBaka;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Readers;

namespace DiarSpeicher.Infrastructure.Metadata;

public partial class MangaBakaIngestService
{
    private async Task RunDownloadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.ResolveDatabasePath());

        var archivePath = Path.Combine(_options.ResolveDatabasePath(), "series.sqlite.zst.part");
        using (var response = await Http.GetAsync(_options.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            if (total > _options.MaxArchiveBytes)
            {
                throw new InvalidOperationException(
                    $"El volcado anunciado ({total} bytes) supera el máximo configurado ({_options.MaxArchiveBytes}).");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[1024 * 256];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;

                if (copied > _options.MaxArchiveBytes)
                {
                    throw new InvalidOperationException("El volcado excede el máximo configurado; descarga abortada.");
                }

                Report(MangaBakaState.Downloading,
                    total is > 0 ? (int)(copied * 100 / total.Value) : null,
                    $"Descargando volcado ({copied / (1024 * 1024)} MB)");
            }
        }

        try
        {
            await using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.None);
            Report(MangaBakaState.Decompressing, null, "Descomprimiendo el volcado");
            await DecompressAsync(archiveStream, "series.sqlite.zst", cancellationToken);
        }
        finally
        {
            TryDelete(archivePath);
        }

        await BuildSearchIndexAsync(cancellationToken);
        Succeed();
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Quedo el volcado parcial {Path} sin borrar; ocupa disco hasta el proximo intento", path);
        }
    }

    private static async Task ExtractSqliteEntryAsync(
        Stream archive,
        string fileName,
        string temporary,
        CancellationToken cancellationToken)
    {
        await using var reader = await ReaderFactory.OpenAsyncReader(archive, cancellationToken: cancellationToken);

        while (await reader.MoveToNextEntryAsync(cancellationToken))
        {
            if (reader.Entry.IsDirectory) continue;

            var key = reader.Entry.Key ?? string.Empty;
            if (!key.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)) continue;

            await using var entryStream = await reader.OpenEntryStreamAsync(cancellationToken);
            await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
            await entryStream.CopyToAsync(output, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"{fileName} no contiene ningún fichero .sqlite.");
    }

    private async Task DecompressAsync(Stream archive, string fileName, CancellationToken cancellationToken)
    {
        var target = _options.ResolveSqlitePath();
        var temporary = target + ".partial";

        try
        {
            if (fileName.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
            {
                await using var decompressed = new DecompressionStream(archive);
                await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
                await decompressed.CopyToAsync(output, cancellationToken);
            }
            else
            {
                await ExtractSqliteEntryAsync(archive, fileName, temporary, cancellationToken);
            }

            // The rename goes last: otherwise a cut mid-decompression would leave a truncated
            // SQLite that the search would take as valid.
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// The dump has no index, so without this every search is a sequential scan of 3.5 GB. The
    /// table is "contentless": it stores only the index and does not repeat the titles, which
    /// are already in <c>series</c>.
    /// </summary>
    private async Task BuildSearchIndexAsync(CancellationToken cancellationToken)
    {
        Report(MangaBakaState.Indexing, null, "Construyendo el índice de búsqueda");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.ResolveSqlitePath(),
            Mode = SqliteOpenMode.ReadWrite
        };

        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, "PRAGMA journal_mode = OFF", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA synchronous = OFF", cancellationToken);
        await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {FtsTable}", cancellationToken);
        await ExecuteAsync(
            connection,
            $"""
            CREATE VIRTUAL TABLE {FtsTable} USING fts5(
                title, romanized_title, native_title, secondary_titles, content=''
            )
            """,
            cancellationToken);

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = $"""
                INSERT INTO {FtsTable}(rowid, title, romanized_title, native_title, secondary_titles)
                SELECT id,
                       COALESCE(title, ''),
                       COALESCE(romanized_title, ''),
                       COALESCE(native_title, ''),
                       COALESCE(secondary_titles, '')
                FROM series
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation("MangaBaka search index rebuilt at {Path}", _options.ResolveSqlitePath());
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void Report(MangaBakaState state, int? percent, string message)
    {
        lock (_sync)
        {
            _state = state;
            _percent = percent;
            _message = message;
        }
    }

    private void Succeed()
    {
        lock (_sync)
        {
            _state = MangaBakaState.Ready;
            _percent = 100;
            _message = "Volcado listo";
        }
    }

    private void Fail(Exception ex, string message)
    {
        _logger.LogError(ex, "{Message}", message);
        lock (_sync)
        {
            _state = MangaBakaState.Failed;
            _percent = null;
            _message = $"{message}: {ex.Message}";
        }
    }
}
