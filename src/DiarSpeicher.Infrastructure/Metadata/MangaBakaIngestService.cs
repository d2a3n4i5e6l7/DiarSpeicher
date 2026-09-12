using DiarSpeicher.Core.Domain.MangaBaka;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Readers;

namespace DiarSpeicher.Infrastructure.Metadata;

/// <summary>
/// Por que no basta un bool: "no he empezado porque ya hay otra ingesta" y "he empezado y
/// ha reventado" son cosas distintas, y colapsarlas hacia false hacia que un fallo de
/// permisos se le contara al usuario como "ya hay una ingesta en curso".
/// </summary>
public enum MangaBakaIngestOutcome
{
    Started,
    Busy,
    Failed
}

public interface IMangaBakaIngestService
{
    MangaBakaStatusDto GetStatus();

    /// <summary>Arranca la descarga en segundo plano. Busy si ya hay una ingesta en curso.</summary>
    MangaBakaIngestOutcome TryStartDownload();

    /// <summary>Ingiere un volcado que trae el usuario, ya sea `.zst` o `.tar.gz`.</summary>
    Task<MangaBakaIngestOutcome> TryImportAsync(Stream archive, string fileName, CancellationToken cancellationToken);

    /// <summary>Ultimo mensaje de estado, que en un fallo es el motivo.</summary>
    string? LastMessage { get; }

    /// <summary>
    /// Trae una portada del catalogo externo para reenviarla desde nuestro origen.
    /// Devuelve null si el host no esta permitido o si el CDN no responde.
    /// </summary>
    Task<(byte[] Data, string ContentType)?> FetchCoverAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// Trae el volcado de MangaBaka a <c>manga_database/</c> y lo deja consultable.
/// <para>
/// El volcado nunca viaja con el repositorio: son ~390 MB comprimidos, ~3,5 GB abiertos y
/// licencia CC BY-NC-SA 4.0. Lo pide el usuario, por el botón o arrastrando el fichero.
/// </para>
/// <para>
/// Solo corre una ingesta a la vez y el estado se guarda en memoria: si el proceso se
/// reinicia a media descarga, al arrancar no hay volcado y se vuelve a empezar, que es
/// preferible a dejar creer que hay un fichero a medias listo para consultar.
/// </para>
/// </summary>
public class MangaBakaIngestService : IMangaBakaIngestService
{
    private const string FtsTable = "series_fts";

    /// <summary>
    /// Cliente propio en vez de IHttpClientFactory: eso obligaria a traer
    /// Microsoft.Extensions.Http a un proyecto que hoy solo depende de EF, SharpCompress,
    /// SkiaSharp y PdfPig. Lo que aporta la factoria —rotacion de DNS, agrupacion de
    /// manejadores— no pinta nada en una descarga manual, ocasional y a un unico host.
    /// El timeout por defecto de HttpClient es de 100 segundos y el volcado son ~390 MB.
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(2) };

    private readonly MangaBakaOptions _options;
    private readonly ILogger<MangaBakaIngestService> _logger;

    private readonly Lock _sync = new();
    private MangaBakaState _state = MangaBakaState.Absent;
    private int? _percent;
    private string? _message;
    private bool _busy;

    public MangaBakaIngestService(
        IOptions<MangaBakaOptions> options,
        ILogger<MangaBakaIngestService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public MangaBakaStatusDto GetStatus()
    {
        var sqlitePath = _options.ResolveSqlitePath();
        var file = new FileInfo(sqlitePath);

        lock (_sync)
        {
            var state = _state;
            if (!_busy)
            {
                state = file.Exists ? MangaBakaState.Ready : MangaBakaState.Absent;
                if (_state == MangaBakaState.Failed) state = MangaBakaState.Failed;
            }

            return new MangaBakaStatusDto
            {
                State = state.ToString(),
                Percent = _percent,
                Message = _message,
                SizeBytes = file.Exists ? file.Length : 0,
                UpdatedAt = file.Exists ? new DateTimeOffset(file.LastWriteTimeUtc) : null,
                Busy = _busy
            };
        }
    }

    public string? LastMessage
    {
        get
        {
            lock (_sync) { return _message; }
        }
    }

    public MangaBakaIngestOutcome TryStartDownload()
    {
        lock (_sync)
        {
            if (_busy) return MangaBakaIngestOutcome.Busy;
            _busy = true;
            _state = MangaBakaState.Downloading;
            _percent = 0;
            _message = "Contactando con mangabaka.org";
        }

        // Deliberadamente desligado de la petición HTTP que lo lanzó: la descarga dura
        // minutos y no puede morir cuando el navegador cierre la conexión.
        _ = Task.Run(async () =>
        {
            try
            {
                await RunDownloadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Fail(ex, "La descarga del volcado falló");
            }
            finally
            {
                lock (_sync) { _busy = false; }
            }
        });

        return MangaBakaIngestOutcome.Started;
    }

    public async Task<MangaBakaIngestOutcome> TryImportAsync(Stream archive, string fileName, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_busy) return MangaBakaIngestOutcome.Busy;
            _busy = true;
            _state = MangaBakaState.Decompressing;
            _percent = null;
            _message = $"Descomprimiendo {fileName}";
        }

        try
        {
            Directory.CreateDirectory(_options.ResolveDatabasePath());
            await DecompressAsync(archive, fileName, cancellationToken);
            await BuildSearchIndexAsync(cancellationToken);
            Succeed();
            return MangaBakaIngestOutcome.Started;
        }
        catch (Exception ex)
        {
            Fail(ex, $"No se pudo ingerir {fileName}");
            return MangaBakaIngestOutcome.Failed;
        }
        finally
        {
            lock (_sync) { _busy = false; }
        }
    }

    /// <summary>
    /// El navegador rechaza las portadas del CDN de MangaBaka con CORP, asi que no puede
    /// pedirlas el propio cliente: las pide el servidor y las sirve desde nuestro origen.
    /// De paso el navegador del usuario deja de hablar con un tercero.
    /// </summary>
    public async Task<(byte[] Data, string ContentType)?> FetchCoverAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !_options.IsAllowedCover(uri))
        {
            return null;
        }

        try
        {
            using var response = await Http.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return (data, contentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not fetch cover {Url}", uri);
            return null;
        }
    }

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

        await using (var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Report(MangaBakaState.Decompressing, null, "Descomprimiendo el volcado");
            await DecompressAsync(archiveStream, "series.sqlite.zst", cancellationToken);
        }

        File.Delete(archivePath);
        await BuildSearchIndexAsync(cancellationToken);
        Succeed();
    }

    /// <summary>
    /// Acepta las dos formas en que llega el volcado: comprimido en crudo (`.zst`) o dentro
    /// de un contenedor (`.tar.gz`), del que se extrae el primer `.sqlite` que aparezca.
    /// </summary>
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
                await using var reader = await ReaderFactory.OpenAsyncReader(archive, cancellationToken: cancellationToken);
                var extracted = false;

                while (await reader.MoveToNextEntryAsync(cancellationToken))
                {
                    if (reader.Entry.IsDirectory) continue;

                    var key = reader.Entry.Key ?? string.Empty;
                    if (!key.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)) continue;

                    await using var entryStream = await reader.OpenEntryStreamAsync(cancellationToken);
                    await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
                    await entryStream.CopyToAsync(output, cancellationToken);
                    extracted = true;
                    break;
                }

                if (!extracted)
                {
                    throw new InvalidOperationException($"{fileName} no contiene ningún fichero .sqlite.");
                }
            }

            // El renombrado va al final: un corte a media descompresión dejaría si no un
            // SQLite truncado que la búsqueda daría por bueno.
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Construye el índice de títulos. El volcado no trae ninguno, así que sin esto cada
    /// búsqueda es un escaneo secuencial de 3,5 GB. La tabla es "contentless": guarda solo
    /// el índice y no repite los títulos, que ya están en <c>series</c>.
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
