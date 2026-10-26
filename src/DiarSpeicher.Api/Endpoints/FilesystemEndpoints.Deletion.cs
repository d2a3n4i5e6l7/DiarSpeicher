using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class FilesystemEndpoints
{
    private static IResult HandleDeletionInspect(
        [FromQuery] string? path,
        HttpContext httpContext,
        [FromServices] ITrashService trash,
        [FromServices] IOptions<LibraryRootsOptions> options)
    {
        if (!IsAllowed(httpContext)) return Forbidden();

        var scope = trash.Inspect(path ?? string.Empty);
        if (scope != null) return Results.Ok(ToDeletionDto(scope));

        return options.Value.TryResolve(path, out var resolved) && !Directory.Exists(resolved) && !File.Exists(resolved)
            ? Results.NotFound(new ApiError("Eso ya no esta en el disco. Se puede quitar del indice sin borrar nada."))
            : ApiResults.Error(NotDeletable, StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> HandleDeleteEntry(
        [FromQuery] string? path,
        HttpContext httpContext,
        [FromServices] ITrashService trash,
        [FromServices] IDiarSpeicherService catalog,
        CancellationToken ct)
    {
        if (!IsAllowed(httpContext)) return Forbidden();

        var outcome = trash.TryMoveToTrash(path ?? string.Empty, out var entry);
        if (outcome != TrashOutcome.Moved)
        {
            return outcome switch
            {
                TrashOutcome.NotFound => Results.NotFound(new ApiError("Eso ya no esta en el disco.")),
                TrashOutcome.NotAllowed => ApiResults.Error(NotDeletable, StatusCodes.Status403Forbidden),
                _ => ApiResults.Error("No se pudo mover a la papelera.", StatusCodes.Status500InternalServerError)
            };
        }

        var user = (AuthUser)httpContext.Items[AuthUserKey]!;
        await catalog.PurgeIndexUnderPathAsync(user, entry!.OriginalPath, ct);

        return Results.Ok(ToTrashDto(entry, trashOptions: null));
    }

    private static DeletionPreviewDto ToDeletionDto(DeletionScope scope) => new()
    {
        Path = scope.Path,
        Name = scope.Name,
        FileCount = scope.FileCount,
        Bytes = scope.Bytes,
        Folders = scope.Children.Select(ToDeletionDto).ToList()
    };

    internal static TrashEntryDto ToTrashDto(TrashEntry entry, TrashOptions? trashOptions)
    {
        var retention = trashOptions?.Retention ?? TimeSpan.FromHours(1);
        var left = entry.DeletedAt + retention - DateTimeOffset.UtcNow;

        return new TrashEntryDto
        {
            Id = entry.Id,
            OriginalPath = entry.OriginalPath,
            Name = entry.Name,
            IsDirectory = entry.IsDirectory,
            FileCount = entry.FileCount,
            Bytes = entry.Bytes,
            DeletedAt = entry.DeletedAt,
            ExpiresInSeconds = left > TimeSpan.Zero ? (int)left.TotalSeconds : 0
        };
    }
}
