using System.Text;
using System.Text.Json;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static class TusEndpoints
{
    private const string AuthUserKey = "AuthUser";
    private const string TusResumableVersion = "1.0.0";
    private const string HeaderUploadOffset = "Upload-Offset";
    private const string HeaderUploadLength = "Upload-Length";
    private const string TusContentType = "application/offset+octet-stream";

    public static RouteGroupBuilder MapTusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/files");

        group.MapMethods("", ["OPTIONS"], HandleOptions);
        group.MapPost("", HandleCreationAsync).DisableAntiforgery();
        group.MapMethods("{id}", ["OPTIONS"], HandleOptions);
        group.MapMethods("{id}", ["HEAD"], HandleHeadAsync);
        group.MapPatch("{id}", HandlePatchAsync).DisableAntiforgery();
        group.MapDelete("{id}", HandleDelete);

        return group;
    }

    private static IResult HandleOptions(
        HttpContext ctx,
        [FromServices] IOptions<StorageOptions> storageOptions)
    {
        var uploadOptions = storageOptions.Value.Upload;
        AddTusHeaders(ctx);
        ctx.Response.Headers.Append("Tus-Version", TusResumableVersion);
        ctx.Response.Headers.Append("Tus-Extension", "creation,termination");
        ctx.Response.Headers.Append("Tus-Max-Size", uploadOptions.MaxFileUploadSize.ToString());

        return Results.NoContent();
    }

    private static async Task<IResult> HandleCreationAsync(
        HttpContext ctx,
        DiarSpeicherDbContext db,
        [FromServices] IOptions<StorageOptions> storageOptions,
        CancellationToken ct)
    {
        AddTusHeaders(ctx);

        var user = ctx.Items[AuthUserKey] as AuthUser;
        if (user == null) return Results.Unauthorized();

        var (prepError, uploadLength) = ValidateUploadPermissionAndLength(ctx, user, storageOptions.Value.Upload);
        if (prepError != null) return prepError;

        var (metaError, libraryId, safeFileName, subpath) = ValidateUploadMetadata(ctx, storageOptions.Value.Upload);
        if (metaError != null) return metaError;

        var library = await db.Libraries.ForUser(user).FirstOrDefaultAsync(l => l.Id == libraryId, ct);
        if (library == null || !Directory.Exists(library.Path))
        {
            return Results.NotFound("Library not found.");
        }

        var (dirError, fullTargetDir) = ValidateTargetDirectory(library.Path, subpath, user);
        if (dirError != null) return dirError;

        var session = new TusSessionRequest(libraryId, subpath, safeFileName, fullTargetDir, uploadLength, user.Id);

        return await CreateUploadSessionAsync(ctx, storageOptions.Value, session, ct);
    }

    private static (IResult? Error, long Length) ValidateUploadPermissionAndLength(
        HttpContext ctx,
        AuthUser user,
        UploadOptions uploadOptions)
    {
        if (!uploadOptions.EnableUpload)
        {
            return (Results.Json(new { error = "Uploads are disabled on this server." }, statusCode: StatusCodes.Status403Forbidden), 0);
        }

        if (!user.HasPermission(Permissions.FileUpload))
        {
            return (Results.Json(new { error = "This account is not allowed to upload files." }, statusCode: StatusCodes.Status403Forbidden), 0);
        }

        if (!ctx.Request.Headers.TryGetValue("Upload-Length", out var lengthValues) ||
            !long.TryParse(lengthValues.FirstOrDefault(), out var uploadLength) ||
            uploadLength <= 0)
        {
            return (Results.BadRequest("Upload-Length header is required and must be greater than 0"), 0);
        }

        if (uploadLength > uploadOptions.MaxFileUploadSize)
        {
            return (Results.Json(new { error = $"File exceeds maximum allowed size of {uploadOptions.MaxFileUploadSize} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge), 0);
        }

        return (null, uploadLength);
    }

    private static (IResult? Error, string LibraryId, string SafeFileName, string? Subpath) ValidateUploadMetadata(
        HttpContext ctx,
        UploadOptions uploadOptions)
    {
        var metadata = ParseUploadMetadata(ctx.Request.Headers["Upload-Metadata"].FirstOrDefault());
        if (!metadata.TryGetValue("libraryId", out var libraryId) || string.IsNullOrWhiteSpace(libraryId))
        {
            return (Results.BadRequest("Metadata 'libraryId' is required."), string.Empty, string.Empty, null);
        }

        if (!metadata.TryGetValue("filename", out var fileName) || string.IsNullOrWhiteSpace(fileName))
        {
            return (Results.BadRequest("Metadata 'filename' is required."), string.Empty, string.Empty, null);
        }

        var safeFileName = Path.GetFileName(fileName);
        if (!_uploadOptionsAllows(uploadOptions, safeFileName))
        {
            return (Results.BadRequest($"Extension for '{safeFileName}' is not accepted."), string.Empty, string.Empty, null);
        }

        metadata.TryGetValue("subpath", out var subpath);
        return (null, libraryId, safeFileName, subpath);
    }

    private static (IResult? Error, string FullTargetDir) ValidateTargetDirectory(
        string libraryPath,
        string? subpath,
        AuthUser user)
    {
        if (!TryResolveTargetDirectory(libraryPath, subpath, out var fullTargetDir))
        {
            return (Results.BadRequest("Invalid subpath."), string.Empty);
        }

        if (!Directory.Exists(fullTargetDir) && !user.HasPermission(Permissions.CreateFolder))
        {
            return (Results.Json(
                new { error = "This account is not allowed to create folders in the library." },
                statusCode: StatusCodes.Status403Forbidden), string.Empty);
        }

        if (!TryEnsureWritableDirectory(fullTargetDir, out var destinationError))
        {
            return (Results.Json(
                new
                {
                    error = $"Upload destination '{fullTargetDir}' is not writable: {destinationError}. " +
                            "Check that the libraries volume is not mounted read-only."
                },
                statusCode: StatusCodes.Status500InternalServerError), string.Empty);
        }

        return (null, fullTargetDir);
    }

    private sealed record TusSessionRequest(
        string LibraryId,
        string? Subpath,
        string SafeFileName,
        string FullTargetDir,
        long UploadLength,
        string UserId);

    private static async Task<IResult> CreateUploadSessionAsync(
        HttpContext ctx,
        StorageOptions storageOptions,
        TusSessionRequest session,
        CancellationToken ct)
    {
        var id = Ulid.NewUlid().ToString();
        var uploadsDir = storageOptions.ResolveUploadsPath();
        Directory.CreateDirectory(uploadsDir);

        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");
        var partPath = Path.Combine(uploadsDir, $"{id}.part");

        var meta = new TusUploadMeta
        {
            UploadId = id,
            LibraryId = session.LibraryId,
            Subpath = session.Subpath,
            FileName = session.SafeFileName,
            FinalPath = Path.Combine(session.FullTargetDir, session.SafeFileName),
            TotalBytes = session.UploadLength,
            PartPath = partPath,
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = session.UserId
        };

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta), ct);
        await using (var fs = File.Create(partPath))
        {
            fs.SetLength(0);
        }

        var location = $"/files/{id}";
        ctx.Response.Headers.Append("Location", location);
        return Results.Created(location, null);
    }

    private static async Task<IResult> HandleHeadAsync(
        string id,
        HttpContext ctx,
        [FromServices] IOptions<StorageOptions> storageOptions,
        CancellationToken ct)
    {
        AddTusHeaders(ctx);

        var uploadsDir = storageOptions.Value.ResolveUploadsPath();
        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");

        if (!File.Exists(metaPath))
        {
            return Results.NotFound();
        }

        TusUploadMeta? meta;
        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct);
            meta = JsonSerializer.Deserialize<TusUploadMeta>(json);
        }
        catch
        {
            return Results.NotFound();
        }

        if (meta == null) return Results.NotFound();

        var partPath = !string.IsNullOrEmpty(meta.PartPath)
            ? meta.PartPath
            : Path.Combine(uploadsDir, $"{id}.part");

        if (!File.Exists(partPath))
        {
            return Results.NotFound();
        }

        var currentLength = new FileInfo(partPath).Length;

        ctx.Response.Headers.Append(HeaderUploadOffset, currentLength.ToString());
        ctx.Response.Headers.Append(HeaderUploadLength, meta.TotalBytes.ToString());
        ctx.Response.Headers.Append("Cache-Control", "no-store");

        return Results.Ok();
    }

    private static async Task<IResult> HandlePatchAsync(
        string id,
        HttpContext ctx,
        [FromServices] IOptions<StorageOptions> storageOptions,
        [FromServices] IScannerQueue scannerQueue,
        CancellationToken ct)
    {
        AddTusHeaders(ctx);

        var user = ctx.Items[AuthUserKey] as AuthUser;
        if (user == null) return Results.Unauthorized();

        var (requestError, requestedOffset) = ValidatePatchRequest(ctx, user);
        if (requestError != null) return requestError;

        var (resolveError, meta, metaPath, partPath) = await ResolveUploadFilesAsync(id, storageOptions.Value.ResolveUploadsPath(), ct);
        if (resolveError != null || meta == null) return resolveError ?? Results.NotFound();

        var fileInfo = new FileInfo(partPath);
        var currentOffset = fileInfo.Length;

        if (requestedOffset != currentOffset)
        {
            ctx.Response.Headers.Append(HeaderUploadOffset, currentOffset.ToString());
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }

        await using (var fs = new FileStream(partPath, FileMode.Open, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            fs.Seek(requestedOffset, SeekOrigin.Begin);
            await ctx.Request.Body.CopyToAsync(fs, 81920, ct);
        }

        fileInfo.Refresh();
        var updatedOffset = fileInfo.Length;
        ctx.Response.Headers.Append(HeaderUploadOffset, updatedOffset.ToString());

        if (updatedOffset >= meta.TotalBytes
            && await FinalizeUploadAsync(meta, partPath, metaPath, scannerQueue, ct) is { } failure)
        {
            return failure;
        }

        return Results.NoContent();
    }

    private static async Task<IResult?> FinalizeUploadAsync(
        TusUploadMeta meta,
        string partPath,
        string metaPath,
        IScannerQueue scannerQueue,
        CancellationToken ct)
    {
        var destinationExisted = File.Exists(meta.FinalPath);

        try
        {
            var destDir = Path.GetDirectoryName(meta.FinalPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Move(partPath, meta.FinalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // El directorio de subidas y las bibliotecas suelen estar en mounts distintos,
            // así que el move degrada a copia y puede dejar el destino a medias. Solo se
            // borra si lo creamos nosotros: un fichero previo no es nuestro para tirarlo.
            if (!destinationExisted)
            {
                TryDelete(meta.FinalPath);
            }

            // La sesión se descarta en lugar de conservarla: con el offset ya completo,
            // cada reintento del cliente volvería a este mismo bloque y al mismo error,
            // dejando un .part por intento en el directorio de subidas.
            TryDelete(partPath);
            TryDelete(metaPath);

            return Results.Json(
                new { error = $"Could not move the upload to its final location: {ex.Message}" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        File.Delete(metaPath);

        await scannerQueue.QueueScanAsync(new ScanRequest(meta.LibraryId), ct);
        return null;
    }

    private static (IResult? Error, long Offset) ValidatePatchRequest(
        HttpContext ctx,
        AuthUser user)
    {
        if (!user.HasPermission(Permissions.FileUpload))
        {
            return (Results.StatusCode(StatusCodes.Status403Forbidden), 0);
        }

        var contentType = ctx.Request.Headers.ContentType.ToString();
        if (!string.Equals(contentType, TusContentType, StringComparison.OrdinalIgnoreCase))
        {
            return (Results.StatusCode(StatusCodes.Status415UnsupportedMediaType), 0);
        }

        var offsetHeader = ctx.Request.Headers[HeaderUploadOffset].ToString();
        if (!long.TryParse(offsetHeader, out var requestedOffset) || requestedOffset < 0)
        {
            return (Results.BadRequest($"Header '{HeaderUploadOffset}' must be a non-negative integer."), 0);
        }

        return (null, requestedOffset);
    }

    private static async Task<(IResult? Error, TusUploadMeta? Meta, string MetaPath, string PartPath)> ResolveUploadFilesAsync(
        string id,
        string uploadsDir,
        CancellationToken ct)
    {
        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");
        if (!File.Exists(metaPath))
        {
            return (Results.NotFound(), null, string.Empty, string.Empty);
        }

        TusUploadMeta? meta;
        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct);
            meta = JsonSerializer.Deserialize<TusUploadMeta>(json);
        }
        catch
        {
            return (Results.NotFound(), null, string.Empty, string.Empty);
        }

        if (meta == null)
        {
            return (Results.NotFound(), null, string.Empty, string.Empty);
        }

        var partPath = !string.IsNullOrEmpty(meta.PartPath)
            ? meta.PartPath
            : Path.Combine(uploadsDir, $"{id}.part");

        if (!File.Exists(partPath))
        {
            return (Results.NotFound(), null, string.Empty, string.Empty);
        }

        return (null, meta, metaPath, partPath);
    }

    private static Task<IResult> HandleDelete(
        string id,
        HttpContext ctx,
        [FromServices] IOptions<StorageOptions> storageOptions)
    {
        AddTusHeaders(ctx);

        var user = ctx.Items[AuthUserKey] as AuthUser;
        if (user == null) return Task.FromResult<IResult>(Results.Unauthorized());

        var uploadsDir = storageOptions.Value.ResolveUploadsPath();
        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");

        if (File.Exists(metaPath))
        {
            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<TusUploadMeta>(metaJson);
                if (meta != null && !string.IsNullOrEmpty(meta.PartPath) && File.Exists(meta.PartPath))
                {
                    File.Delete(meta.PartPath);
                }
            }
            catch
            {
                // Ignorar error al limpiar partPath
            }
            File.Delete(metaPath);
        }

        var legacyPart = Path.Combine(uploadsDir, $"{id}.part");
        if (File.Exists(legacyPart)) File.Delete(legacyPart);

        return Task.FromResult<IResult>(Results.NoContent());
    }

    private static void AddTusHeaders(HttpContext ctx)
    {
        ctx.Response.Headers.Append("Tus-Resumable", TusResumableVersion);
    }

    private static Dictionary<string, string> ParseUploadMetadata(string? rawMetadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawMetadata)) return result;

        var pairs = rawMetadata.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;

            var key = parts[0];
            var value = "";
            if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
            {
                try
                {
                    var bytes = Convert.FromBase64String(parts[1]);
                    value = Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    value = parts[1];
                }
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Creates the directory if it is missing and writes a throwaway probe file to it.
    /// Directory.Exists says nothing about whether writes are accepted: a read-only bind
    /// mount reports every directory in it as existing.
    /// </summary>
    private static bool TryEnsureWritableDirectory(string directory, out string error)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var probePath = Path.Combine(directory, $".diarspeicher-write-probe-{Guid.NewGuid():N}");
            using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                probe.WriteByte(0);
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Best-effort cleanup: must not mask the error that triggered it.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichero bloqueado o sin permiso: el temporal se queda, que es preferible a
            // tapar el error que provoco la limpieza.
        }
    }

    private static bool _uploadOptionsAllows(UploadOptions options, string fileName)
    {
        return options.IsExtensionAllowed(fileName);
    }

    private static bool TryResolveTargetDirectory(string libraryPath, string? subpath, out string fullTargetDir)
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

        return fullTargetDir.Equals(fullLibraryPath, StringComparison.OrdinalIgnoreCase) ||
               fullTargetDir.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TusUploadMeta
    {
        public string UploadId { get; set; } = string.Empty;
        public string LibraryId { get; set; } = string.Empty;
        public string? Subpath { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public string FinalPath { get; set; } = string.Empty;
        public string PartPath { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
