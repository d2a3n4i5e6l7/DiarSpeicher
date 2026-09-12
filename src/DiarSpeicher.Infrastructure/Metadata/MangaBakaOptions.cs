namespace DiarSpeicher.Infrastructure.Metadata;

/// <summary>
/// Dónde vive el volcado de MangaBaka y de dónde se baja. Se enlaza de la sección
/// "MangaBaka" de la configuración.
/// </summary>
public class MangaBakaOptions
{
    public const string SectionName = "MangaBaka";

    /// <summary>
    /// Carpeta del volcado. Nunca se versiona: son gigabytes con licencia ajena, y el
    /// usuario la puebla desde la interfaz.
    /// <para>
    /// Vacia por defecto a proposito: se rellena con &lt;RootPath de Storage&gt;/manga_database,
    /// que es el volumen montado y con permisos. El directorio de trabajo del proceso no
    /// sirve —en el contenedor es /home/nonroot, que no es escribible ni persiste—.
    /// </para>
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>Volcado comprimido que publica MangaBaka.</summary>
    public string DownloadUrl { get; set; } = "https://mangabaka.org/data/database/series.sqlite.zst";

    /// <summary>
    /// Tope de lo que se acepta por descarga o subida. El volcado ronda los 400 MB
    /// comprimido; el margen cubre que crezca sin dejar la puerta abierta de par en par.
    /// </summary>
    public long MaxArchiveBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Hosts de los que se aceptan portadas. Es una lista blanca y no un filtro cualquiera
    /// porque el proxy recibe la URL del cliente: sin acotarlo seria un proxy abierto con
    /// el que pedirle al servidor que hable con cualquier direccion de su red interna.
    /// </summary>
    public List<string> CoverHosts { get; set; } = ["mangabaka.dev", "cdn.mangabaka.dev"];

    /// <summary>True si la URL es una portada que este servidor acepta reenviar.</summary>
    public bool IsAllowedCover(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && CoverHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    public string ResolveDatabasePath() => Path.GetFullPath(
        string.IsNullOrWhiteSpace(DatabasePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "manga_database")
            : DatabasePath);

    /// <summary>El SQLite ya descomprimido, con el índice de búsqueda construido encima.</summary>
    public string ResolveSqlitePath() => Path.Combine(ResolveDatabasePath(), "series.sqlite");
}
