using System.Text.Json;
using DiarSpeicher.Api.Serialization;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class TusEndpoints
{
    private static async Task<IResult> HandleCreationAsync(
        HttpContext ctx,
        [FromServices] ISqliteConnectionFactory connectionFactory,
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

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = libraryId;
        const string sql = $"""
            SELECT l."Id", l."Path"
            FROM "Libraries" l
            WHERE l."Id" = @Id AND {CatalogSqlFilters.LibraryVisible}
            LIMIT 1;
            """;

        var library = await conn.QueryFirstOrDefaultAsync<Library>(sql, parameters);
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
            return (ApiResults.Error("Uploads are disabled on this server.", StatusCodes.Status403Forbidden), 0);
        }

        if (!user.HasPermission(Permissions.FileUpload))
        {
            return (ApiResults.Error("This account is not allowed to upload files.", StatusCodes.Status403Forbidden), 0);
        }

        if (!ctx.Request.Headers.TryGetValue("Upload-Length", out var lengthValues) ||
            !long.TryParse(lengthValues.FirstOrDefault(), out var uploadLength) ||
            uploadLength <= 0)
        {
            return (Results.BadRequest("Upload-Length header is required and must be greater than 0"), 0);
        }

        if (uploadLength > uploadOptions.MaxFileUploadSize)
        {
            return (ApiResults.Error($"File exceeds maximum allowed size of {uploadOptions.MaxFileUploadSize} bytes.", StatusCodes.Status413PayloadTooLarge), 0);
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
            return (ApiResults.Error("This account is not allowed to create folders in the library.", StatusCodes.Status403Forbidden), string.Empty);
        }

        if (!TryEnsureWritableDirectory(fullTargetDir, out var destinationError))
        {
            return (ApiResults.Error(
                $"Upload destination '{fullTargetDir}' is not writable: {destinationError}. " +
                "Check that the libraries volume is not mounted read-only.",
                StatusCodes.Status500InternalServerError), string.Empty);
        }

        return (null, fullTargetDir);
    }

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

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, PersistedJsonContext.Default.TusUploadMeta), ct);
        await using (var fs = File.Create(partPath))
        {
            fs.SetLength(0);
        }

        var location = $"/files/{id}";
        ctx.Response.Headers.Append("Location", location);
        return Results.Created(location, null);
    }
}
