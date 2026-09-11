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

        var uploadOptions = storageOptions.Value.Upload;
        if (!uploadOptions.EnableUpload)
        {
            return Results.Json(new { error = "Uploads are disabled on this server." }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!user.HasPermission(Permissions.FileUpload))
        {
            return Results.Json(new { error = "This account is not allowed to upload files." }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!ctx.Request.Headers.TryGetValue("Upload-Length", out var lengthValues) ||
            !long.TryParse(lengthValues.FirstOrDefault(), out var uploadLength) ||
            uploadLength <= 0)
        {
            return Results.BadRequest("Upload-Length header is required and must be greater than 0");
        }

        if (uploadLength > uploadOptions.MaxFileUploadSize)
        {
            return Results.Json(new { error = $"File exceeds maximum allowed size of {uploadOptions.MaxFileUploadSize} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var metadata = ParseUploadMetadata(ctx.Request.Headers["Upload-Metadata"].FirstOrDefault());
        if (!metadata.TryGetValue("libraryId", out var libraryId) || string.IsNullOrWhiteSpace(libraryId))
        {
            return Results.BadRequest("Metadata 'libraryId' is required.");
        }

        if (!metadata.TryGetValue("filename", out var fileName) || string.IsNullOrWhiteSpace(fileName))
        {
            return Results.BadRequest("Metadata 'filename' is required.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (!_uploadOptionsAllows(uploadOptions, safeFileName))
        {
            return Results.BadRequest($"Extension for '{safeFileName}' is not accepted.");
        }

        metadata.TryGetValue("subpath", out var subpath);

        var library = await db.Libraries.ForUser(user).FirstOrDefaultAsync(l => l.Id == libraryId, ct);
        if (library == null || !Directory.Exists(library.Path))
        {
            return Results.NotFound("Library not found.");
        }

        if (!TryResolveTargetDirectory(library.Path, subpath, out var fullTargetDir))
        {
            return Results.BadRequest("Invalid subpath.");
        }

        if (!Directory.Exists(fullTargetDir) && !user.HasPermission(Permissions.CreateFolder))
        {
            return Results.Json(
                new { error = "This account is not allowed to create folders in the library." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // El destino se comprueba aquí y no al finalizar: un directorio montado en solo
        // lectura existe igualmente, y sin esta comprobación el fallo aparecía en el último
        // PATCH, cuando el fichero entero ya se había transferido.
        if (!TryEnsureWritableDirectory(fullTargetDir, out var destinationError))
        {
            return Results.Json(
                new
                {
                    error = $"Upload destination '{fullTargetDir}' is not writable: {destinationError}. " +
                            "Check that the libraries volume is not mounted read-only."
                },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var uploadsDir = storageOptions.Value.ResolveUploadsPath();
        Directory.CreateDirectory(uploadsDir);

        var uploadId = Guid.NewGuid().ToString("N");
        var partPath = Path.Combine(uploadsDir, $"{uploadId}.part");
        var metaPath = Path.Combine(uploadsDir, $"{uploadId}.meta");
        var finalPath = Path.Combine(fullTargetDir, safeFileName);

        var uploadMeta = new TusUploadMeta
        {
            UploadId = uploadId,
            LibraryId = libraryId,
            Subpath = subpath,
            FileName = safeFileName,
            TotalBytes = uploadLength,
            FinalPath = finalPath,
            UserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var metaJson = JsonSerializer.Serialize(uploadMeta);
        await File.WriteAllTextAsync(metaPath, metaJson, ct);

        await File.WriteAllBytesAsync(partPath, [], ct);

        var fileLocation = $"/api/v2/files/{uploadId}";
        ctx.Response.Headers.Append("Location", fileLocation);
        ctx.Response.Headers.Append(HeaderUploadLength, uploadLength.ToString());

        return Results.Created(fileLocation, null);
    }

    private static async Task<IResult> HandleHeadAsync(
        string id,
        HttpContext ctx,
        [FromServices] IOptions<StorageOptions> storageOptions,
        CancellationToken ct)
    {
        AddTusHeaders(ctx);

        var user = ctx.Items[AuthUserKey] as AuthUser;
        if (user == null) return Results.Unauthorized();

        var uploadsDir = storageOptions.Value.ResolveUploadsPath();
        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");
        var partPath = Path.Combine(uploadsDir, $"{id}.part");

        if (!File.Exists(metaPath) || !File.Exists(partPath))
        {
            return Results.NotFound();
        }

        var metaJson = await File.ReadAllTextAsync(metaPath, ct);
        var meta = JsonSerializer.Deserialize<TusUploadMeta>(metaJson);
        if (meta == null) return Results.NotFound();

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

        var uploadOptions = storageOptions.Value.Upload;
        if (!uploadOptions.EnableUpload)
        {
            return Results.Json(new { error = "Uploads are disabled on this server." }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!user.HasPermission(Permissions.FileUpload))
        {
            return Results.Json(new { error = "This account is not allowed to upload files." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
        {
            sizeFeature.MaxRequestBodySize = uploadOptions.MaxRequestBytes;
        }

        var contentType = ctx.Request.ContentType;
        if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("application/offset+octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (!ctx.Request.Headers.TryGetValue(HeaderUploadOffset, out var offsetValues) ||
            !long.TryParse(offsetValues.FirstOrDefault(), out var requestedOffset))
        {
            return Results.BadRequest("Upload-Offset header is required");
        }

        var uploadsDir = storageOptions.Value.ResolveUploadsPath();
        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");
        var partPath = Path.Combine(uploadsDir, $"{id}.part");

        if (!File.Exists(metaPath) || !File.Exists(partPath))
        {
            return Results.NotFound();
        }

        var metaJson = await File.ReadAllTextAsync(metaPath, ct);
        var meta = JsonSerializer.Deserialize<TusUploadMeta>(metaJson);
        if (meta == null) return Results.NotFound();

        var fileInfo = new FileInfo(partPath);
        var currentOffset = fileInfo.Length;

        if (requestedOffset != currentOffset)
        {
            ctx.Response.Headers.Append(HeaderUploadOffset, currentOffset.ToString());
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }

        // Escritura directa al offset indicado con buffer optimizado de 80 KB
        await using (var fs = new FileStream(partPath, FileMode.Open, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            fs.Seek(requestedOffset, SeekOrigin.Begin);
            await ctx.Request.Body.CopyToAsync(fs, 81920, ct);
        }

        fileInfo.Refresh();
        var updatedOffset = fileInfo.Length;
        ctx.Response.Headers.Append(HeaderUploadOffset, updatedOffset.ToString());

        // Comprobar si se completó la subida total
        if (updatedOffset >= meta.TotalBytes)
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
        }

        return Results.NoContent();
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
        var partPath = Path.Combine(uploadsDir, $"{id}.part");

        if (File.Exists(partPath)) File.Delete(partPath);
        if (File.Exists(metaPath)) File.Delete(metaPath);

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
        public string UserId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
