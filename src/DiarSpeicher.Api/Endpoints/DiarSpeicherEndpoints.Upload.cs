using System.Runtime.CompilerServices;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DiarSpeicher.Api.Endpoints;

public static partial class DiarSpeicherEndpoints
{
    private static async Task<IResult> HandleLibraryUpload(
        string id,
        HttpContext httpContext,
        [FromServices] IDiarSpeicherService service,
        [FromServices] IOptions<StorageOptions> storageOptions,
        CancellationToken ct)
    {
        var user = (AuthUser)httpContext.Items[AuthUserKey]!;
        var (validationError, boundary) = ValidateUploadRequest(httpContext, storageOptions.Value.Upload);
        if (validationError != null) return validationError;

        var reader = new MultipartReader(boundary, httpContext.Request.Body);
        var (formSubpath, firstFile) = await ReadUntilFirstFileAsync(reader, ct);
        var querySubpath = httpContext.Request.Query["subpath"].FirstOrDefault();
        var subpath = string.IsNullOrEmpty(querySubpath) ? formSubpath : querySubpath;

        var files = ReadMultipartFilesAsync(reader, firstFile, ct);
        var result = await service.UploadToLibraryAsync(user, id, subpath, files, ct);
        return MapUploadResult(result);
    }

    private static (IResult? Error, string Boundary) ValidateUploadRequest(HttpContext httpContext, UploadOptions uploadOptions)
    {
        if (!httpContext.Request.HasFormContentType)
            return (Results.BadRequest("Expected multipart/form-data"), string.Empty);

        if (!uploadOptions.EnableUpload)
        {
            return (ApiResults.Error("Uploads are disabled on this server.", StatusCodes.Status403Forbidden), string.Empty);
        }

        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
        {
            sizeFeature.MaxRequestBodySize = uploadOptions.MaxRequestBytes;
        }

        var mediaType = MediaTypeHeaderValue.Parse(httpContext.Request.ContentType);
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return (Results.BadRequest("Missing multipart boundary"), string.Empty);
        }

        return (null, boundary);
    }

    private static IResult MapUploadResult(UploadResult result)
    {
        if (result.Outcome == UploadOutcome.Success) return Results.Ok(result.Response);

        var message = result.Message ?? result.Outcome.ToString();
        return result.Outcome switch
        {
            UploadOutcome.UploadDisabled or UploadOutcome.PermissionDenied => ApiResults.Error(message, StatusCodes.Status403Forbidden),
            UploadOutcome.LibraryNotFound => Results.NotFound(new ApiError(message)),
            UploadOutcome.FileTooLarge => ApiResults.Error(message, StatusCodes.Status413PayloadTooLarge),
            _ => Results.BadRequest(new ApiError(message))
        };
    }

    private static async Task<(string? Subpath, DiarSpeicherUploadFileInput? FirstFile)> ReadUntilFirstFileAsync(
        MultipartReader reader,
        CancellationToken ct)
    {
        string? subpath = null;
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync(ct)) != null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) || cd == null)
            {
                continue;
            }

            if (cd.IsFormDisposition())
            {
                subpath = await ReadSubpathAsync(section, cd, ct) ?? subpath;
                continue;
            }

            var file = ToFileInput(section, cd);
            if (file != null) return (subpath, file);
        }

        return (subpath, null);
    }

    private static async Task<string?> ReadSubpathAsync(
        MultipartSection section,
        ContentDispositionHeaderValue cd,
        CancellationToken ct)
    {
        if (cd.Name.Value != "subpath") return null;

        using var reader = new StreamReader(section.Body);
        var val = await reader.ReadToEndAsync(ct);
        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }

    private static DiarSpeicherUploadFileInput? ToFileInput(
        MultipartSection section,
        ContentDispositionHeaderValue cd)
    {
        var rawName = cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value;
        if (string.IsNullOrWhiteSpace(rawName)) return null;

        var name = Path.GetFileName(rawName);
        return new DiarSpeicherUploadFileInput { FileName = name, Content = section.Body };
    }

    private static async IAsyncEnumerable<DiarSpeicherUploadFileInput> ReadMultipartFilesAsync(
        MultipartReader reader,
        DiarSpeicherUploadFileInput? firstFile,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (firstFile != null) yield return firstFile;

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) != null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) || cd == null)
            {
                continue;
            }

            var file = ToFileInput(section, cd);
            if (file != null) yield return file;
        }
    }
}
