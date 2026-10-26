using System.Text.Json;
using DiarSpeicher.Api.Serialization;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class TusEndpoints
{
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
            meta = JsonSerializer.Deserialize(json, PersistedJsonContext.Default.TusUploadMeta);
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
            // The uploads directory and the libraries usually live on different mounts, so the
            // move degrades to a copy and can leave a partial destination. It is only removed
            // if we created it: a pre-existing file is not ours to discard.
            if (!destinationExisted)
            {
                TryDelete(meta.FinalPath);
            }

            // The session is discarded rather than kept: with the offset already complete,
            // every client retry would hit this same block and error, leaving one .part per
            // attempt in the uploads directory.
            TryDelete(partPath);
            TryDelete(metaPath);

            return ApiResults.Error($"Could not move the upload to its final location: {ex.Message}", StatusCodes.Status500InternalServerError);
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
            meta = JsonSerializer.Deserialize(json, PersistedJsonContext.Default.TusUploadMeta);
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
}
