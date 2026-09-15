using System.Collections.Concurrent;
using System.Threading.Channels;
using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Instantanea del escaneo de una biblioteca, con lo que hace falta para una estimacion.
/// </summary>
public sealed record ScanSnapshot(
    ScanProgressEvent Progress,
    DateTimeOffset StartedAt,
    bool Finished)
{
    /// <summary>Encolado pero sin empezar: hay otro escaneo delante.</summary>
    public bool Queued { get; init; }

    public TimeSpan Elapsed => DateTimeOffset.UtcNow - StartedAt;

    /// <summary>
    /// Lo que falta, extrapolando de lo que ya se tardo. Null mientras no haya al menos una
    /// serie hecha: dar una estimacion con cero muestras es inventar un numero.
    /// </summary>
    public TimeSpan? Eta
    {
        get
        {
            if (Finished || Queued || Progress.CompletedSeries <= 0 || Progress.TotalSeries <= 0) return null;

            var remaining = Progress.TotalSeries - Progress.CompletedSeries;
            if (remaining <= 0) return TimeSpan.Zero;

            var perSeries = Elapsed.TotalSeconds / Progress.CompletedSeries;

            return TimeSpan.FromSeconds(perSeries * remaining);
        }
    }
}

public interface IScanProgressHub
{
    ScanSnapshot? GetSnapshot(string libraryId);

    /// <summary>Todo lo encolado o en marcha, para pintar de gris lo que espera turno.</summary>
    IReadOnlyCollection<ScanSnapshot> GetActive();

    /// <summary>
    /// La cola tiene un solo lector, asi que lo encolado espera a que termine lo anterior.
    /// Sin esto no habria forma de distinguir "en cola" de "no se ha pedido".
    /// </summary>
    void MarkQueued(string libraryId);

    /// <summary>Suscriptor efimero: se cierra al irse el cliente y no deja rastro.</summary>
    IAsyncEnumerable<ScanSnapshot> SubscribeAsync(string libraryId, CancellationToken ct);
}

public sealed class NullScanProgressHub : IScanProgressHub
{
    public ScanSnapshot? GetSnapshot(string libraryId) => null;
    public IReadOnlyCollection<ScanSnapshot> GetActive() => [];
    public void MarkQueued(string libraryId) { }
    public async IAsyncEnumerable<ScanSnapshot> SubscribeAsync(string libraryId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>
/// Reparte el progreso del escaner a los clientes conectados por SSE.
/// <para>
/// Vive en memoria y no persiste: si el proceso se reinicia a mitad de un escaneo, el
/// escaneo tambien muere, asi que no hay nada que recuperar.
/// </para>
/// </summary>
public sealed class ScanProgressHub : IScanProgressHub, IScanProgressPublisher
{
    private readonly ConcurrentDictionary<string, ScanSnapshot> _latest = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ScanSnapshot>>> _subscribers =
        new(StringComparer.Ordinal);

    public ScanSnapshot? GetSnapshot(string libraryId) =>
        _latest.TryGetValue(libraryId, out var snapshot) ? snapshot : null;

    public IReadOnlyCollection<ScanSnapshot> GetActive() =>
        _latest.Values.Where(s => !s.Finished).ToList();

    public void MarkQueued(string libraryId)
    {
        var queued = new ScanSnapshot(
            new ScanProgressEvent { JobId = libraryId, LibraryId = libraryId, Phase = ScanPhase.Started },
            DateTimeOffset.UtcNow,
            Finished: false)
        { Queued = true };

        // Sobrescribe un escaneo anterior ya terminado, nunca uno en marcha.
        _latest.AddOrUpdate(libraryId, queued, (_, previous) => previous.Finished ? queued : previous);
    }

    public ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default)
    {
        var finished = progress.Phase is ScanPhase.Completed or ScanPhase.Failed;

        var snapshot = _latest.AddOrUpdate(
            progress.LibraryId,
            _ => new ScanSnapshot(progress, DateTimeOffset.UtcNow, finished),
            // El arranque se conserva entre eventos: es la unica base para la estimacion.
            (_, previous) => previous with { Progress = progress, Finished = finished, Queued = false });

        if (_subscribers.TryGetValue(progress.LibraryId, out var channels))
        {
            foreach (var channel in channels.Values)
            {
                // Descartar si el cliente no lee: un navegador lento no puede frenar el escaneo.
                channel.Writer.TryWrite(snapshot);
            }
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ScanSnapshot> SubscribeAsync(
        string libraryId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        // DropOldest: con un progreso lo unico que importa es el ultimo valor.
        var channel = Channel.CreateBounded<ScanSnapshot>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var channels = _subscribers.GetOrAdd(libraryId, _ => new ConcurrentDictionary<Guid, Channel<ScanSnapshot>>());
        channels[id] = channel;

        try
        {
            // Lo que ya haya, primero: un cliente que llega a mitad del escaneo no puede
            // quedarse en blanco hasta el siguiente evento.
            if (_latest.TryGetValue(libraryId, out var current))
            {
                yield return current;
            }

            await foreach (var snapshot in channel.Reader.ReadAllAsync(ct))
            {
                yield return snapshot;
            }
        }
        finally
        {
            channels.TryRemove(id, out _);
            if (channels.IsEmpty)
            {
                _subscribers.TryRemove(libraryId, out _);
            }
        }
    }
}
