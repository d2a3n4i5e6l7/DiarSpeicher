using DiarSpeicher.Infrastructure.Metadata;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DiarSpeicher.Api.Endpoints;

public static partial class MetadataEndpoints
{
    /// <summary>
    /// The dump is around 390 MB, so it is streamed with MultipartReader rather than
    /// ReadFormAsync: the latter buffers the whole body and has its own 128 MB cap. Kestrel's
    /// limit, 30 MB by default, has to be raised too.
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
            return Results.BadRequest(new ApiError("Se espera multipart/form-data con el volcado."));
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
            return Results.BadRequest(new ApiError("Falta el boundary del multipart."));
        }

        var reader = new MultipartReader(boundary, httpContext.Request.Body);
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            var result = await ProcessDumpSectionAsync(section, ingest, ct);
            if (result != null) return result;
        }

        return Results.BadRequest(new ApiError("No llegó ningún fichero."));
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
            return Results.BadRequest(new ApiError("Solo se aceptan volcados .zst, .tar.gz o .tgz."));
        }

        return ToResult(await ingest.TryImportAsync(section.Body, name, ct), ingest);
    }
}
