using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace DiarSpeicher.Infrastructure.Filesystem;

/// <summary>
/// Un CBR convertido: de dónde salió, en qué CBZ quedó y si el original sigue en disco.
/// El escáner usa el par de rutas para reapuntar el medio ya indexado en vez de darlo por
/// perdido y crear otro, que costaría el progreso de lectura del usuario.
/// </summary>
public sealed record ArchiveConversion(string SourcePath, string TargetPath, bool SourceDeleted);

public interface IArchiveConversionService
{
    Task<IReadOnlyList<ArchiveConversion>> ConvertDirectoryAsync(
        string directory,
        bool hardDeleteSource,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Convierte los CBR de una carpeta a CBZ.
/// <para>
/// El motivo no es estético: un CBR obliga a descomprimir con RAR en cada petición de
/// página, mientras que un CBZ se sirve entrada a entrada sin descomprimir el resto. Para
/// un lector que pide páginas sueltas, esa diferencia se nota en cada salto.
/// </para>
/// <para>
/// Las páginas ya vienen comprimidas (JPEG, PNG, WebP), así que el ZIP se escribe en modo
/// almacenamiento: volver a comprimirlas gasta CPU para no ganar bytes.
/// </para>
/// </summary>
public class ArchiveConversionService : IArchiveConversionService
{
    private static readonly string[] SourceExtensions = [".cbr", ".rar"];

    private readonly ILogger<ArchiveConversionService> _logger;

    public ArchiveConversionService(ILogger<ArchiveConversionService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArchiveConversion>> ConvertDirectoryAsync(
        string directory,
        bool hardDeleteSource,
        CancellationToken cancellationToken = default)
    {
        var conversions = new List<ArchiveConversion>();
        if (!Directory.Exists(directory)) return conversions;

        var sources = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.ChangeExtension(source, ".cbz");
            if (target is null) continue;

            try
            {
                // Un CBZ ya presente se respeta: puede venir de una conversión anterior o
                // ser una copia que el usuario dejó ahí. Reconvertir lo pisaría.
                if (!File.Exists(target) && !await TryConvertAsync(source, target, cancellationToken))
                {
                    continue;
                }

                var deleted = false;
                if (hardDeleteSource && File.Exists(source))
                {
                    File.Delete(source);
                    deleted = true;
                }

                conversions.Add(new ArchiveConversion(source, target, deleted));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not convert {Source} to CBZ; leaving the original in place", source);
            }
        }

        return conversions;
    }

    /// <summary>
    /// Escribe primero a un temporal y renombra al final: un corte a mitad de conversión
    /// dejaría si no un CBZ truncado que el escáner indexaría como libro válido.
    /// </summary>
    private async Task<bool> TryConvertAsync(string source, string target, CancellationToken cancellationToken)
    {
        var temporary = target + ".converting";
        var entriesWritten = 0;

        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                await using var archive = await RarArchive.OpenAsyncArchive(source, cancellationToken: cancellationToken);

                await foreach (var entry in archive.EntriesAsync.WithCancellation(cancellationToken))
                {
                    if (entry.IsDirectory) continue;

                    var key = entry.Key;
                    if (string.IsNullOrEmpty(key)) continue;

                    var zipEntry = zip.CreateEntry(key.Replace('\\', '/'), CompressionLevel.NoCompression);
                    await using var entryStream = await entry.OpenEntryStreamAsync(cancellationToken);
                    await using var zipStream = zipEntry.Open();
                    await entryStream.CopyToAsync(zipStream, cancellationToken);
                    entriesWritten++;
                }
            }

            if (entriesWritten == 0)
            {
                _logger.LogWarning("{Source} produced no entries; not replacing it with a CBZ", source);
                File.Delete(temporary);
                return false;
            }

            File.Move(temporary, target, overwrite: true);
            _logger.LogInformation("Converted {Source} to {Target} ({Entries} entries)", source, target, entriesWritten);
            return true;
        }
        catch
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                    // El temporal se queda; no vale la pena tumbar el escaneo por él.
                }
            }
            throw;
        }
    }
}
