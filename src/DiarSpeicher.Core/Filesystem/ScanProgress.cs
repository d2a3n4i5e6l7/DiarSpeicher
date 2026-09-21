using DiarSpeicher.Core.Domain.Catalog;

namespace DiarSpeicher.Core.Filesystem;

public enum ScanPhase
{
    Started,
    WalkingLibrary,
    ProcessingSeries,

    ProcessingMedia,

    Completed,
    Failed
}

/// <summary>
/// Incremental progress for one scan. <see cref="JobId"/> is the library id, so a client
/// subscribing to a scan it triggered knows the id up front.
/// </summary>
public record ScanProgressEvent
{
    public string JobId { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
    public ScanPhase Phase { get; init; }
    public int CompletedSeries { get; init; }
    public int TotalSeries { get; init; }
    public string? CurrentSeries { get; init; }

    /// <summary>
    /// Grano de tomo dentro de la serie en curso. Con solo el grano de serie, una biblioteca
    /// de una sola carpeta con treinta tomos se pasa el escaneo entero diciendo "0 de 1", y
    /// el cliente no tiene con que dibujar por donde va.
    /// </summary>
    public int CompletedMedia { get; init; }
    public int TotalMedia { get; init; }
    public string? CurrentMedia { get; init; }

    /// <summary>
    /// Rutas de los tomos que la serie en curso va a crear, en orden de proceso. Va la ruta y no
    /// el nombre porque el nombre se repite: dos carpetas distintas tienen cada una su
    /// "Volumen 1", y con el nombre como clave el cliente los tomaria por el mismo tomo.
    /// </summary>
    public IReadOnlyList<string> PendingMedia { get; init; } = [];

    /// <summary>
    /// Nombre de fichero del tomo que se esta leyendo, de la misma forma que los de
    /// <see cref="PendingMedia"/>. <see cref="CurrentMedia"/> no sirve para casarlos: cuando el
    /// tomo trae metadatos, ese lleva el titulo y no el nombre del archivo.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Ficheros que el escaner esta leyendo ahora mismo, por nombre, como los de
    /// <see cref="PendingMedia"/>. Se anuncian antes del analisis —que es la parte lenta— para
    /// que el hueco se encienda mientras se lee y no cuando ya ha terminado.
    /// </summary>
    public IReadOnlyList<string> ReadingFiles { get; init; } = [];

    /// <summary>
    /// Tomos que acaban de entrar en el catalogo, ya mapeados. Van completos para que el
    /// cliente pinte la tarjeta definitiva —portada, paginas y tamaño— en cuanto llega el
    /// evento, sin volver a pedir el catalogo entero cada vez que termina una serie.
    /// </summary>
    public IReadOnlyList<DiarSpeicherMediaDto> CreatedMedia { get; init; } = [];

    /// <summary>Series que acaban de entrar en el catalogo, por el mismo motivo.</summary>
    public IReadOnlyList<DiarSpeicherSeriesDto> CreatedSeries { get; init; } = [];

    /// <summary>Identifica la serie sin depender del nombre, que no es unico.</summary>
    public string? CurrentSeriesId { get; init; }

    public string? Message { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>0 when the series count is not yet known, so a client can render a spinner.</summary>
    public double Percentage => TotalSeries <= 0
        ? 0
        : Math.Round(CompletedSeries / (double)TotalSeries * 100, 2);
}

public interface IScanProgressPublisher
{
    ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default);
}
