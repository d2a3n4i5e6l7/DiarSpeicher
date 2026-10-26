using DiarSpeicher.Core.Domain.MangaBaka;

namespace DiarSpeicher.Infrastructure.Metadata;

/// <summary>
/// A bool is not enough: "not started because another ingest is running" and "started and blew
/// up" are different, and collapsing both into false reports a permission failure to the user
/// as "an ingest is already running".
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

    MangaBakaIngestOutcome TryStartDownload();

    Task<MangaBakaIngestOutcome> TryImportAsync(Stream archive, string fileName, CancellationToken cancellationToken);

    MangaBakaIngestOutcome TryStartImportFile(string tempFilePath, string fileName);

    string? LastMessage { get; }

    Task<(byte[] Data, string ContentType)?> FetchCoverAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// The dump never ships with the repository: ~390 MB compressed, ~3.5 GB uncompressed and
/// licensed CC BY-NC-SA 4.0. The user requests it, through the button or by dropping the file.
///
/// Only one ingest runs at a time and state lives in memory: if the process restarts
/// mid-download, at startup there is no dump and it starts over, which beats letting anyone
/// believe a half-written file is ready to query.
/// </summary>
public partial class MangaBakaIngestService : IMangaBakaIngestService
{
    private const string FtsTable = "series_fts";

    /// <summary>
    /// Own client rather than IHttpClientFactory: what the factory brings (DNS rotation,
    /// handler pooling) is irrelevant for a manual, occasional download from a single host.
    /// HttpClient's default timeout is 100 seconds and the dump is ~390 MB.
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

        // Deliberately detached from the HTTP request that started it: the download takes
        // minutes and must not die when the browser closes the connection.
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

    public MangaBakaIngestOutcome TryStartImportFile(string tempFilePath, string fileName)
    {
        lock (_sync)
        {
            if (_busy) return MangaBakaIngestOutcome.Busy;
            _busy = true;
            _state = MangaBakaState.Decompressing;
            _percent = null;
            _message = $"Descomprimiendo {fileName}";
        }

        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(_options.ResolveDatabasePath());
                await using (var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 81920, useAsync: true))
                {
                    await DecompressAsync(stream, fileName, CancellationToken.None);
                }
                await BuildSearchIndexAsync(CancellationToken.None);
                Succeed();
            }
            catch (Exception ex)
            {
                Fail(ex, $"No se pudo ingerir {fileName}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                }
                catch
                {
                    // Best effort: a leftover temporary file is harmless.
                }

                lock (_sync) { _busy = false; }
            }
        });

        return MangaBakaIngestOutcome.Started;
    }

    /// <summary>
    /// The browser rejects MangaBaka CDN covers through CORP, so the client cannot request them
    /// itself: the server fetches them and serves them from our origin. As a side effect, the
    /// user's browser stops talking to a third party.
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
}
