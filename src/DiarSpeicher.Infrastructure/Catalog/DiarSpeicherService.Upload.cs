namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public Task<UploadResult> UploadToLibraryAsync(
        AuthUser user,
        string libraryId,
        string? subpath,
        IEnumerable<DiarSpeicherUploadFileInput> files,
        CancellationToken ct = default)
    {
        return UploadToLibraryAsync(user, libraryId, subpath, ToAsync(files), ct);

        static async IAsyncEnumerable<DiarSpeicherUploadFileInput> ToAsync(IEnumerable<DiarSpeicherUploadFileInput> source)
        {
            foreach (var item in source)
            {
                yield return item;
            }
        }
    }

    public async Task<UploadResult> UploadToLibraryAsync(
        AuthUser user,
        string libraryId,
        string? subpath,
        IAsyncEnumerable<DiarSpeicherUploadFileInput> files,
        CancellationToken ct = default)
    {
        var (rejection, library, fullTargetDir) = await ResolveUploadTargetAsync(user, libraryId, subpath, ct);
        if (rejection != null)
        {
            return rejection;
        }

        Directory.CreateDirectory(fullTargetDir);

        var savedFiles = new List<UploadedFileDto>();
        await foreach (var file in files.WithCancellation(ct))
        {
            if (string.IsNullOrWhiteSpace(file.FileName) || file.Content == null) continue;

            var (rejected, saved) = await SaveUploadedFileAsync(file, fullTargetDir, ct);
            if (rejected != null)
            {
                return rejected;
            }

            savedFiles.Add(saved);
        }

        if (savedFiles.Count == 0)
        {
            return UploadResult.Fail(UploadOutcome.NoAcceptedFiles, "No valid files were provided.");
        }

        await _scannerQueue.QueueScanAsync(new ScanRequest(library.Id), ct);

        return UploadResult.Ok(new DiarSpeicherUploadResponseDto
        {
            UploadedCount = savedFiles.Count,
            Files = savedFiles,
            ScanJobTriggered = true
        });
    }

    /// <summary>
    /// Escribe un fichero en la carpeta de destino aplicando el limite de tamano. Si lo
    /// excede se borra lo ya escrito: un fichero a medias en la biblioteca lo recogeria el
    /// siguiente escaneo como si fuese un libro valido.
    /// </summary>
    private async Task<(UploadResult? Rejected, UploadedFileDto Saved)> SaveUploadedFileAsync(
        DiarSpeicherUploadFileInput file,
        string targetDir,
        CancellationToken ct)
    {
        if (!_uploadOptions.IsExtensionAllowed(file.FileName))
        {
            return (UploadResult.Fail(
                UploadOutcome.ExtensionNotAllowed,
                $"The extension of '{Path.GetFileName(file.FileName)}' is not accepted."), null!);
        }

        var safeName = Path.GetFileName(file.FileName);
        var destPath = Path.Combine(targetDir, safeName);

        try
        {
            var written = await WriteWithLimitAsync(file.Content, destPath, _uploadOptions.MaxFileUploadSize, ct);
            return (null, new UploadedFileDto { Name = safeName, Path = destPath, Size = written });
        }
        catch (UploadTooLargeException)
        {
            DeleteQuietly(destPath);
            return (UploadResult.Fail(
                UploadOutcome.FileTooLarge,
                $"'{safeName}' exceeds the maximum size of {_uploadOptions.MaxFileUploadSize} bytes."), null!);
        }
    }

    private async Task<(UploadResult? Rejection, Library Library, string TargetDir)> ResolveUploadTargetAsync(
        AuthUser user,
        string libraryId,
        string? subpath,
        CancellationToken ct)
    {
        static (UploadResult?, Library, string) Reject(UploadOutcome outcome, string message) =>
            (UploadResult.Fail(outcome, message), null!, string.Empty);

        if (!_uploadOptions.EnableUpload)
        {
            return Reject(UploadOutcome.UploadDisabled, "Uploads are disabled on this server.");
        }

        if (!user.HasPermission(Permissions.FileUpload))
        {
            return Reject(UploadOutcome.PermissionDenied, "This account is not allowed to upload files.");
        }

        var library = await _db.Libraries.ForUser(user)
            .FirstOrDefaultAsync(l => l.Id == libraryId, ct);

        if (library == null || !Directory.Exists(library.Path))
        {
            return Reject(UploadOutcome.LibraryNotFound, "Library not found.");
        }

        if (!TryResolveTargetDirectory(library.Path, subpath, out var fullTargetDir))
        {
            return Reject(UploadOutcome.LibraryNotFound, "Library not found.");
        }

        if (!Directory.Exists(fullTargetDir) && !user.HasPermission(Permissions.CreateFolder))
        {
            return Reject(
                UploadOutcome.PermissionDenied,
                "This account is not allowed to create folders in the library.");
        }

        return (null, library, fullTargetDir);
    }

    private bool TryResolveTargetDirectory(
        string libraryPath,
        string? subpath,
        out string fullTargetDir)
    {
        var cleanSubpath = (subpath ?? string.Empty).Trim('/', '\\');
        var targetDir = string.IsNullOrEmpty(cleanSubpath)
            ? libraryPath
            : Path.Combine(libraryPath, cleanSubpath);

        fullTargetDir = Path.GetFullPath(targetDir);
        var fullLibraryPath = Path.GetFullPath(libraryPath);

        var libraryPrefix = fullLibraryPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullLibraryPath
            : fullLibraryPath + Path.DirectorySeparatorChar;

        var isInsideLibrary =
            fullTargetDir.Equals(fullLibraryPath, StringComparison.OrdinalIgnoreCase) ||
            fullTargetDir.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase);

        if (!isInsideLibrary)
        {
            _logger.LogWarning("Attempted path traversal upload to {TargetDir} outside {LibraryPath}", fullTargetDir, fullLibraryPath);
            return false;
        }

        return true;
    }

    private static async Task<long> WriteWithLimitAsync(Stream source, string destPath, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;

        await using var fs = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new UploadTooLargeException();
            }

            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: ignore failures if the file is locked or permissions prevent deletion
        }
    }
}

public sealed class UploadTooLargeException : Exception;
