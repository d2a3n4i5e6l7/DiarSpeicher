using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem;

namespace DiarSpeicher.Api.Endpoints;

public static partial class FilesystemEndpoints
{
    private static IResult CreateFolder(CreateFolderInput input, LibraryRootsOptions options)
    {
        var name = input.Name.Trim();

        // A name only, never a path: with a separator or ".." the Combine would escape the
        // parent the client claims to be using.
        if (name.Length == 0
            || name is "." or ".."
            || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Results.BadRequest(new ApiError("Nombre de carpeta no valido."));
        }

        if (!options.TryResolve(input.Parent, out var parent) || !Directory.Exists(parent))
        {
            return ApiResults.Error(OutsideRoots, StatusCodes.Status403Forbidden);
        }

        var target = Path.Combine(parent, name);
        if (!options.TryResolve(target, out var resolved))
        {
            return ApiResults.Error(OutsideRoots, StatusCodes.Status403Forbidden);
        }

        try
        {
            Directory.CreateDirectory(resolved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ApiError("No se pudo crear la carpeta: " + e.Message));
        }

        return Results.Ok(ToEntry(resolved));
    }

    private static FolderEntryDto ToEntry(string directory)
    {
        var entry = new FolderEntryDto
        {
            Name = Path.GetFileName(directory),
            Path = directory
        };

        // Counting children can fail on permissions in any single folder; that must not break
        // the whole listing, it only leaves the expand arrow without a hint.
        try
        {
            entry.HasChildren = Directory.EnumerateDirectories(directory).Any();
            entry.FileCount = Directory.EnumerateFiles(directory).Take(MaxEntries).Count();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            entry.Readable = false;
        }

        return entry;
    }

    private static string? ResolveParent(string resolved, LibraryRootsOptions options)
    {
        var parent = Path.GetDirectoryName(resolved);

        return parent != null && options.TryResolve(parent, out var checkedParent) ? checkedParent : null;
    }

    private static FolderListingDto Empty() => new()
    {
        Path = string.Empty,
        Entries = []
    };

    private static bool IsAllowed(HttpContext httpContext) =>
        httpContext.Items[AuthUserKey] is AuthUser user && user.HasPermission(Permissions.ManageLibrary);

    private static IResult Forbidden() =>
        ApiResults.Error("This account is not allowed to manage libraries.", StatusCodes.Status403Forbidden);
}
