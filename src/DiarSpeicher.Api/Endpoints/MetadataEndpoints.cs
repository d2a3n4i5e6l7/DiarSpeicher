using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Metadata;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DiarSpeicher.Api.Endpoints;

/// <summary>
/// Gestión del volcado de MangaBaka y emparejado de series.
/// <para>
/// Va por REST y no por GraphQL a propósito: subir un fichero de cientos de megabytes y
/// sondear el progreso de una descarga son justamente las dos cosas que GraphQL hace mal.
/// Las consultas del catálogo sí viven en el esquema GraphQL, junto al resto.
/// </para>
/// </summary>
public static class MetadataEndpoints
{
    private const string AuthUserKey = "AuthUser";

    public static RouteGroupBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/metadata");

        group.MapGet("/status", (
            [FromServices] IMangaBakaIngestService ingest,
            [FromServices] IMangaBakaCatalog catalog,
            CancellationToken ct) => GetStatusAsync(ingest, catalog, ct));

        group.MapPost("/download", (
            HttpContext httpContext,
            [FromServices] IMangaBakaIngestService ingest) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return ToResult(ingest.TryStartDownload(), ingest);
        });

        group.MapPost("/import", HandleImportAsync).DisableAntiforgery();
        group.MapPost("/import/init", HandleImportInitAsync).DisableAntiforgery();
        group.MapPost("/import/chunk", HandleImportChunkAsync).DisableAntiforgery();
        group.MapPost("/import/complete", HandleImportCompleteAsync).DisableAntiforgery();

        // Sin permiso de gestion a proposito: es una imagen, y la ve cualquiera que ya
        // puede ver la ficha. Lo que si se acota es el host de origen, en IsAllowedCover.
        group.MapGet("/cover", async (
            [FromQuery] string url,
            HttpContext httpContext,
            [FromServices] IMangaBakaIngestService ingest,
            CancellationToken ct) =>
        {
            var cover = await ingest.FetchCoverAsync(url, ct);
            if (cover is null) return Results.NotFound();

            // Las portadas del volcado no cambian entre refrescos: cachearlas una semana
            // evita que cada apertura del dialogo vuelva a salir a internet.
            httpContext.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(cover.Value.Data, cover.Value.ContentType);
        });

        group.MapGet("/series/{seriesId}/candidates", async (
            string seriesId,
            HttpContext httpContext,
            [FromServices] ISeriesMetadataMatcher matcher,
            [FromQuery] int limit = 10,
            CancellationToken ct = default) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var candidates = await matcher.SuggestAsync(seriesId, limit, ct);
            return Results.Ok(candidates);
        });

        group.MapPut("/series/{seriesId}/match/{mangaBakaId:int}", async (
            string seriesId,
            int mangaBakaId,
            HttpContext httpContext,
            [FromServices] ISeriesMetadataMatcher matcher,
            CancellationToken ct) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var applied = await matcher.ApplyAsync(seriesId, mangaBakaId, ct);
            return applied ? Results.Ok(new { matched = true }) : Results.NotFound();
        });

        group.MapDelete("/series/{seriesId}/match", async (
            string seriesId,
            HttpContext httpContext,
            [FromServices] ISeriesMetadataMatcher matcher,
            CancellationToken ct) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var cleared = await matcher.ClearAsync(seriesId, ct);
            return cleared ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }

    private static async Task<IResult> GetStatusAsync(
        IMangaBakaIngestService ingest,
        IMangaBakaCatalog catalog,
        CancellationToken ct)
    {
        var status = ingest.GetStatus();
        if (catalog.IsAvailable)
        {
            status.SeriesCount = await catalog.CountAsync(ct);
        }
        return Results.Ok(status);
    }

    /// <summary>
    /// El volcado ronda los 390 MB, asi que se lee en streaming con MultipartReader y no con
    /// ReadFormAsync: esta ultima bufferiza el cuerpo entero y ademas tiene su propio tope
    /// de 128 MB. Tambien hay que levantar el limite de Kestrel, que por defecto son 30 MB.
    /// </summary>
    private static async Task<IResult> HandleImportAsync(
        HttpContext httpContext,
        [FromServices] IMangaBakaIngestService ingest,
        [FromServices] IOptions<MangaBakaOptions> options,
        CancellationToken ct)
    {
        if (!IsAllowed(httpContext)) return Forbidden();

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Se espera multipart/form-data con el volcado." });
        }

        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
        {
            sizeFeature.MaxRequestBodySize = options.Value.MaxArchiveBytes;
        }

        var mediaType = MediaTypeHeaderValue.Parse(httpContext.Request.ContentType);
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return Results.BadRequest(new { error = "Falta el boundary del multipart." });
        }

        var reader = new MultipartReader(boundary, httpContext.Request.Body);
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            var result = await ProcessDumpSectionAsync(section, ingest, ct);
            if (result != null) return result;
        }

        return Results.BadRequest(new { error = "No llegó ningún fichero." });
    }

    private static async Task<IResult?> ProcessDumpSectionAsync(
        MultipartSection section,
        IMangaBakaIngestService ingest,
        CancellationToken ct)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) return null;
        if (!disposition.FileName.HasValue && !disposition.FileNameStar.HasValue) return null;

        var raw = disposition.FileNameStar.HasValue ? disposition.FileNameStar.Value : disposition.FileName.Value;
        var name = Path.GetFileName(raw ?? string.Empty);

        if (!IsAcceptedDumpExtension(name))
        {
            return Results.BadRequest(new { error = "Solo se aceptan volcados .zst, .tar.gz o .tgz." });
        }

        return ToResult(await ingest.TryImportAsync(section.Body, name, ct), ingest);
    }

    public sealed record MetadataImportInitRequest(string FileName, long TotalBytes);
    public sealed record MetadataImportCompleteRequest(string UploadId, string FileName);

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
            return Results.Conflict(new { error = "Ya hay una ingesta del volcado en curso." });
        }

        var fileName = Path.GetFileName(request.FileName ?? string.Empty);
        if (!IsAcceptedDumpExtension(fileName))
        {
            return Results.BadRequest(new { error = "Solo se aceptan volcados .zst, .tar.gz o .tgz." });
        }

        if (request.TotalBytes <= 0 || request.TotalBytes > options.Value.MaxArchiveBytes)
        {
            return Results.BadRequest(new { error = $"El tamaño ({request.TotalBytes} bytes) supera el máximo configurado ({options.Value.MaxArchiveBytes})." });
        }

        var uploadId = Guid.NewGuid().ToString("N");
        var dbDir = options.Value.ResolveDatabasePath();
        Directory.CreateDirectory(dbDir);
        var partPath = Path.Combine(dbDir, $"import_{uploadId}.part");

        await File.WriteAllBytesAsync(partPath, [], ct);

        const int chunkSize = 50 * 1024 * 1024;
        return Results.Ok(new { uploadId, chunkSize });
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
            return Results.BadRequest(new { error = "Identificador de subida no válido." });
        }

        var dbDir = options.Value.ResolveDatabasePath();
        var partPath = Path.Combine(dbDir, $"import_{uploadId}.part");
        if (!File.Exists(partPath))
        {
            return Results.NotFound(new { error = "Sesión de subida no encontrada o expirada." });
        }

        var fileInfo = new FileInfo(partPath);
        if (fileInfo.Length != offset)
        {
            return Results.Conflict(new { error = "Desincronización de offset.", currentOffset = fileInfo.Length });
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
        return Results.Ok(new { offset = fileInfo.Length });
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
            return Results.BadRequest(new { error = "Identificador de subida no válido." });
        }

        var fileName = Path.GetFileName(request.FileName ?? string.Empty);
        var dbDir = options.Value.ResolveDatabasePath();
        var partPath = Path.Combine(dbDir, $"import_{request.UploadId}.part");
        if (!File.Exists(partPath))
        {
            return Results.NotFound(new { error = "Fichero de subida no encontrado." });
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

    private static bool IsAcceptedDumpExtension(string name) =>
        name.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Un fallo de la ingesta no es un conflicto: devolver 409 con "ya hay una en curso"
    /// mandaba al usuario a buscar una ingesta fantasma cuando lo que habia era un error de
    /// permisos o un fichero corrupto. El motivo real sale en el cuerpo.
    /// </summary>
    private static IResult ToResult(MangaBakaIngestOutcome outcome, IMangaBakaIngestService ingest) => outcome switch
    {
        MangaBakaIngestOutcome.Started => Results.Accepted("/api/v2/metadata/status"),
        MangaBakaIngestOutcome.Busy => Results.Conflict(new { error = "Ya hay una ingesta del volcado en curso." }),
        _ => Results.Json(
            new { error = ingest.LastMessage ?? "La ingesta del volcado falló." },
            statusCode: StatusCodes.Status500InternalServerError)
    };

    /// <summary>
    /// Poblar el catálogo externo y reescribir fichas es administración de biblioteca, así
    /// que se pide el mismo permiso que para crearlas.
    /// </summary>
    private static bool IsAllowed(HttpContext httpContext) =>
        httpContext.Items[AuthUserKey] is AuthUser user && user.HasPermission(Permissions.ManageLibrary);

    private static IResult Forbidden() =>
        Results.Json(new { error = "This account is not allowed to manage library metadata." },
            statusCode: StatusCodes.Status403Forbidden);
}
