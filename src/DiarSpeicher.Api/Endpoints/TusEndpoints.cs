using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class TusEndpoints
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

    private sealed record TusSessionRequest(
        string LibraryId,
        string? Subpath,
        string SafeFileName,
        string FullTargetDir,
        long UploadLength,
        string UserId);
    internal sealed class TusUploadMeta
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
