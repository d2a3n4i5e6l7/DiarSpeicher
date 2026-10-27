using DiarSpeicher.Core.Domain.MangaBaka;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Metadata;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class MetadataEndpoints
{
    private static async Task<IResult> HandleImportInitAsync(
        MetadataImportInitRequest request,
        HttpContext httpContext,
        [FromServices] IMangaBakaIngestService ingest,
        [FromServices] IOptions<MangaBakaOptions> options,
        CancellationToken ct)
    {
        if (!IsAllowed(httpContext)) return Forbidden();

        if (ingest.GetStatus().Busy)
        {
            return Results.Conflict(new ApiError("Ya hay una ingesta del volcado en curso."));
        }

        var fileName = Path.GetFileName(request.FileName ?? string.Empty);
        if (!DumpFormats.IsAccepted(fileName))
        {
            return Results.BadRequest(new ApiError("Solo se aceptan volcados .zst, .tar.gz o .tgz."));
        }

        if (request.TotalBytes <= 0 || request.TotalBytes > options.Value.MaxArchiveBytes)
        {
            return Results.BadRequest(new ApiError($"El tamaño ({request.TotalBytes} bytes) supera el máximo configurado ({options.Value.MaxArchiveBytes})."));
        }

        var uploadId = Guid.NewGuid().ToString("N");
        var dbDir = options.Value.ResolveDatabasePath();
        Directory.CreateDirectory(dbDir);
        var partPath = Path.Combine(dbDir, $"import_{uploadId}.part");

        await File.WriteAllBytesAsync(partPath, [], ct);

        const int chunkSize = 50 * 1024 * 1024;
        return Results.Ok(new ChunkedUploadSessionResponse(uploadId, chunkSize));
    }

    private static async Task<IResult> HandleImportChunkAsync(
        [FromQuery] string uploadId,
        [FromQuery] long offset,
        HttpContext httpContext,
        [FromServices] IOptions<MangaBakaOptions> options,
        CancellationToken ct)
    {
        if (!IsAllowed(httpContext)) return Forbidden();

        if (string.IsNullOrWhiteSpace(uploadId) || !uploadId.All(char.IsLetterOrDigit))
        {
            return Results.BadRequest(new ApiError("Identificador de subida no válido."));
        }

        var dbDir = options.Value.ResolveDatabasePath();
        var partPath = Path.Combine(dbDir, $"import_{uploadId}.part");
        if (!File.Exists(partPath))
        {
            return Results.NotFound(new ApiError("Sesión de subida no encontrada o expirada."));
        }

        var fileInfo = new FileInfo(partPath);
        if (fileInfo.Length != offset)
        {
            return Results.Conflict(new OffsetConflictError("Desincronización de offset.", fileInfo.Length));
        }

        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
        {
            sizeFeature.MaxRequestBodySize = 75 * 1024 * 1024;
        }

        await using (var fs = new FileStream(partPath, FileMode.Open, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            fs.Seek(offset, SeekOrigin.Begin);
            await httpContext.Request.Body.CopyToAsync(fs, 81920, ct);
        }

        fileInfo.Refresh();
        return Results.Ok(new UploadOffsetResponse(fileInfo.Length));
    }

    private static IResult HandleImportCompleteAsync(
        MetadataImportCompleteRequest request,
        HttpContext httpContext,
        [FromServices] IMangaBakaIngestService ingest,
        [FromServices] IOptions<MangaBakaOptions> options,
        [FromServices] ILoggerFactory loggerFactory)
    {
        if (!IsAllowed(httpContext)) return Forbidden();

        if (string.IsNullOrWhiteSpace(request.UploadId) || !request.UploadId.All(char.IsLetterOrDigit))
        {
            return Results.BadRequest(new ApiError("Identificador de subida no válido."));
        }

        var fileName = Path.GetFileName(request.FileName ?? string.Empty);
        var dbDir = options.Value.ResolveDatabasePath();
        var partPath = Path.Combine(dbDir, $"import_{request.UploadId}.part");
        if (!File.Exists(partPath))
        {
            return Results.NotFound(new ApiError("Fichero de subida no encontrado."));
        }

        var outcome = ingest.TryStartImportFile(partPath, fileName);
        if (outcome != MangaBakaIngestOutcome.Started)
        {
            var logger = loggerFactory.CreateLogger("MetadataImport");
            try
            {
                File.Delete(partPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "La subida {UploadId} no arranco ({Outcome}) y su parcial {Path} sigue en disco", request.UploadId, outcome, partPath);
            }
        }

        return ToResult(outcome, ingest);
    }

    /// <summary>
    /// An ingest failure is not a conflict: answering 409 "one already running" sends the user
    /// hunting for a phantom ingest when the cause is a permission error or a corrupt file. The
    /// real reason goes in the body.
    /// </summary>
    private static IResult ToResult(MangaBakaIngestOutcome outcome, IMangaBakaIngestService ingest) => outcome switch
    {
        MangaBakaIngestOutcome.Started => Results.Accepted("/api/v2/metadata/status"),
        MangaBakaIngestOutcome.Busy => Results.Conflict(new ApiError("Ya hay una ingesta del volcado en curso.")),
        _ => ApiResults.Error(ingest.LastMessage ?? "La ingesta del volcado falló.", StatusCodes.Status500InternalServerError)
    };

    /// <summary>
    /// Populating the external catalog and rewriting records is library administration, so it
    /// requires the same permission as creating them.
    /// </summary>
    private static bool IsAllowed(HttpContext httpContext) =>
        httpContext.Items[AuthUserKey] is AuthUser user && user.HasPermission(Permissions.ManageLibrary);

    private static IResult Forbidden() =>
        ApiResults.Error("This account is not allowed to manage library metadata.", StatusCodes.Status403Forbidden);
}
