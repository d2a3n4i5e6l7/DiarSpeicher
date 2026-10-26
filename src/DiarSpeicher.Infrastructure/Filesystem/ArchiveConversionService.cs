using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace DiarSpeicher.Infrastructure.Filesystem;

public sealed record ArchiveConversion(string SourcePath, string TargetPath, bool SourceDeleted);

public interface IArchiveConversionService
{
    Task<IReadOnlyList<ArchiveConversion>> ConvertDirectoryAsync(
        string directory,
        bool hardDeleteSource,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The reason is not cosmetic: a CBR forces a RAR decompression on every page request, while a
/// CBZ serves entry by entry without decompressing the rest. For a reader requesting single
/// pages that difference shows on every jump.
///
/// Pages are already compressed (JPEG, PNG, WebP), so the ZIP is written in store mode:
/// compressing them again costs CPU without saving bytes.
/// </summary>
public class ArchiveConversionService : IArchiveConversionService
{
    private static readonly string[] SourceExtensions = [".cbr", ".rar"];

    private readonly ILogger<ArchiveConversionService> _logger;
    private readonly ITrashService _trash;

    public ArchiveConversionService(ILogger<ArchiveConversionService> logger, ITrashService? trash = null)
    {
        _logger = logger;
        _trash = trash ?? new NullTrashService();
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
                // An existing CBZ is respected: it may come from an earlier conversion or be a
                // copy the user left there. Converting again would overwrite it.
                if (!File.Exists(target) && !await TryConvertAsync(source, target, cancellationToken))
                {
                    continue;
                }

                var deleted = false;
                if (hardDeleteSource && File.Exists(source))
                {
                    deleted = TryRetireSource(source);
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
    /// To the trash, not File.Delete: this runs only inside the scan with nobody confirming
    /// anything, so a badly converted CBR or a corrupt CBZ would take the original with no way
    /// back.
    /// </summary>
    private bool TryRetireSource(string source)
    {
        if (_trash.TryMoveToTrash(source, out _) == TrashOutcome.Moved) return true;

        _logger.LogWarning("No se pudo retirar el CBR {Source}; se deja en su sitio", source);
        return false;
    }

    /// <summary>
    /// Writes to a temporary file first and renames at the end: otherwise a cut mid-conversion
    /// would leave a truncated CBZ that the scanner would index as a valid book.
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
                    await using var zipStream = await zipEntry.OpenAsync(cancellationToken);
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
                    // The temp file stays; it is not worth breaking the scan over it.
                }
            }
            throw;
        }
    }
}
