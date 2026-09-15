using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

/// <summary>
/// Explorador de carpetas para elegir la ruta de una biblioteca sin escribirla a ciegas.
/// <para>
/// Lista en vivo, sin cache. Leer los hijos de un directorio es una llamada al sistema y
/// siempre es la verdad; una copia en base de datos solo anadiria el problema de quedarse
/// vieja. El indice hace falta para <em>buscar por nombre</em> en el arbol entero, que es
/// otra cosa y no se resuelve aqui.
/// </para>
/// <para>
/// Cuanto sale de aqui esta encerrado en <see cref="LibraryRootsOptions"/>. Sin esa
/// jaula esto seria un lector del sistema de ficheros del contenedor para cualquiera con
/// <c>ManageLibrary</c>.
/// </para>
/// </summary>
public static class FilesystemEndpoints
{
    private const string AuthUserKey = "AuthUser";

    /// <summary>
    /// Tope de entradas por respuesta. Una carpeta con decenas de miles de subcarpetas
    /// existe (descargas planas), y devolverlas todas bloquea el navegador sin ayudar a
    /// nadie a elegir.
    /// </summary>
    private const int MaxEntries = 1000;

    private const string OutsideRoots = "Esa ruta esta fuera de las carpetas permitidas.";

    private const string NotDeletable =
        "Esa ruta no se puede borrar: esta fuera de las carpetas permitidas, es una raiz o es la propia papelera.";

    public static RouteGroupBuilder MapFilesystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/filesystem");

        group.MapGet("/roots", (
            HttpContext httpContext,
            [FromServices] IOptions<LibraryRootsOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var roots = options.Value.ResolveRoots()
                .Select(r => new FolderRootDto
                {
                    Path = r,
                    Name = Path.GetFileName(r) is { Length: > 0 } name ? name : r,
                    Mounted = Directory.Exists(r)
                })
                .ToList();

            return Results.Ok(roots);
        });

        group.MapGet("/index", (
            HttpContext httpContext,
            [FromServices] IFolderIndex index) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Results.Ok(index.GetStatus());
        });

        group.MapPost("/index", (
            HttpContext httpContext,
            [FromServices] IFolderIndex index) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return index.TryStartRefresh()
                ? Results.Accepted(value: index.GetStatus())
                : Results.Json(new { error = "Ya hay un repaso en marcha." }, statusCode: StatusCodes.Status409Conflict);
        });

        group.MapGet("/search", (
            [FromQuery] string? q,
            [FromQuery] int? limit,
            HttpContext httpContext,
            [FromServices] IFolderIndex index) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Results.Ok(index.Search(q ?? string.Empty, limit ?? 60));
        });

        // Contar antes de destruir: lo que se enseña en el aviso sale de aqui, no de una
        // estimacion del cliente.
        group.MapGet("/deletion", (
            [FromQuery] string? path,
            HttpContext httpContext,
            [FromServices] ITrashService trash,
            [FromServices] IOptions<LibraryRootsOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var scope = trash.Inspect(path ?? string.Empty);
            if (scope != null) return Results.Ok(ToDeletionDto(scope));

            // Que la ruta no exista no es lo mismo que no estar permitida, y el usuario
            // necesita saber cual de las dos es.
            return options.Value.TryResolve(path, out var resolved) && !Directory.Exists(resolved) && !File.Exists(resolved)
                ? Results.NotFound(new { error = "Eso ya no esta en el disco. Se puede quitar del indice sin borrar nada." })
                : Results.Json(new { error = NotDeletable }, statusCode: StatusCodes.Status403Forbidden);
        });

        group.MapDelete("/entry", async (
            [FromQuery] string? path,
            HttpContext httpContext,
            [FromServices] ITrashService trash,
            [FromServices] IDiarSpeicherService catalog,
            CancellationToken ct) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var outcome = trash.TryMoveToTrash(path ?? string.Empty, out var entry);
            if (outcome != TrashOutcome.Moved)
            {
                return outcome switch
                {
                    TrashOutcome.NotFound => Results.NotFound(new { error = "Eso ya no esta en el disco." }),
                    TrashOutcome.NotAllowed => Results.Json(new { error = NotDeletable }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.Json(new { error = "No se pudo mover a la papelera." }, statusCode: StatusCodes.Status500InternalServerError)
                };
            }

            // Sin esto las filas se quedan apuntando a una ruta muerta: el tomo sigue
            // saliendo en "anadido reciente" y su miniatura se sigue sirviendo.
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            await catalog.PurgeIndexUnderPathAsync(user, entry!.OriginalPath, ct);

            return Results.Ok(ToTrashDto(entry, trashOptions: null));
        });

        group.MapGet("/preview", (
            [FromQuery] string? path,
            [FromQuery] string? pattern,
            HttpContext httpContext,
            [FromServices] IOptions<LibraryRootsOptions> options,
            [FromServices] IDirectoryScanner scanner) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Preview(path, pattern, options.Value, scanner);
        });

        group.MapGet("/disk", (
            [FromQuery] string? path,
            HttpContext httpContext,
            [FromServices] IOptions<LibraryRootsOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return options.Value.TryResolve(path, out var resolved)
                ? Results.Ok(ReadDisk(resolved))
                : Results.Json(new { error = OutsideRoots }, statusCode: StatusCodes.Status403Forbidden);
        });

        group.MapPost("/folder", (
            [FromBody] CreateFolderInput input,
            HttpContext httpContext,
            [FromServices] IOptions<LibraryRootsOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return CreateFolder(input, options.Value);
        });

        group.MapGet("/browse", (
            [FromQuery] string? path,
            HttpContext httpContext,
            [FromServices] IOptions<LibraryRootsOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Browse(path, options.Value);
        });

        return group;
    }

    private static IResult Browse(string? path, LibraryRootsOptions options)
    {
        var roots = options.ResolveRoots();

        var target = path;
        if (string.IsNullOrWhiteSpace(target))
        {
            target = roots.FirstOrDefault(Directory.Exists) ?? roots.FirstOrDefault();
            if (target == null) return Results.Ok(Empty());
        }

        if (!options.TryResolve(target, out var resolved))
        {
            return Results.Json(
                new { error = OutsideRoots },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!Directory.Exists(resolved))
        {
            return Results.NotFound(new { error = "La carpeta no existe o no esta montada." });
        }

        List<FolderEntryDto> entries;
        try
        {
            entries = Directory.EnumerateDirectories(resolved)
                // Las ocultas no se listan: en un volumen de manga son .stfolder, .@__thumb
                // y demas ruido de sincronizacion, nunca una biblioteca.
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .Select(ToEntry)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(
                new { error = "Sin permiso para leer esa carpeta." },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (IOException)
        {
            return Results.Json(
                new { error = "No se pudo leer esa carpeta." },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(new FolderListingDto
        {
            Path = resolved,
            Parent = ResolveParent(resolved, options),
            IsRoot = roots.Contains(resolved, StringComparer.Ordinal),
            Entries = entries,
            Truncated = entries.Count >= MaxEntries
        });
    }

    /// <summary>
    /// Ensaya el escaneo sin tocar la base de datos. Llama al escaner de verdad, no a una
    /// copia de sus reglas: una vista previa que reimplementa la clasificacion acaba
    /// mintiendo en cuanto alguien cambia el escaner.
    /// </summary>
    private static IResult Preview(
        string? path,
        string? pattern,
        LibraryRootsOptions options,
        IDirectoryScanner scanner)
    {
        if (!options.TryResolve(path, out var resolved))
        {
            return Results.Json(new { error = OutsideRoots }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!Directory.Exists(resolved))
        {
            return Results.NotFound(new { error = "La carpeta no existe o no esta montada." });
        }

        var isCollectionBased = string.Equals(pattern, nameof(LibraryPattern.CollectionBased), StringComparison.OrdinalIgnoreCase);
        var walked = scanner.WalkLibrary(resolved, isCollectionBased, []);

        var preview = new ScanPreviewDto
        {
            Path = resolved,
            Pattern = isCollectionBased ? nameof(LibraryPattern.CollectionBased) : nameof(LibraryPattern.SeriesBased)
        };

        var paths = walked.SeriesToCreate.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var seriesPath in paths)
        {
            var walkedSeries = scanner.WalkSeries(seriesPath, new Dictionary<string, long>(), [], paths);
            var count = walkedSeries.MediaToCreate.Count;

            preview.Series.Add(new PreviewSeriesDto
            {
                Name = Path.GetFileName(seriesPath) is { Length: > 0 } name ? name : seriesPath,
                Path = seriesPath,
                VolumeCount = count,
                IsRoot = string.Equals(seriesPath, resolved, StringComparison.OrdinalIgnoreCase)
            });

            preview.TotalVolumes += count;
        }

        return Results.Ok(preview);
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

    private static DiskUsageDto ReadDisk(string path)
    {
        try
        {
            // En Unix esto resuelve por statvfs sobre la ruta, asi que un bind mount
            // reporta el sistema de ficheros del host que hay detras, no el del contenedor.
            var drive = new DriveInfo(path);

            return new DiskUsageDto
            {
                Path = path,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
                Available = true
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DiskUsageDto { Path = path, Available = false };
        }
    }

    private static IResult CreateFolder(CreateFolderInput input, LibraryRootsOptions options)
    {
        var name = input.Name.Trim();

        // Solo un nombre, nunca una ruta: con un separador o un ".." el Combine saldria
        // del padre que el cliente dice estar usando.
        if (name.Length == 0
            || name == "." || name == ".."
            || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Results.BadRequest(new { error = "Nombre de carpeta no valido." });
        }

        if (!options.TryResolve(input.Parent, out var parent) || !Directory.Exists(parent))
        {
            return Results.Json(new { error = OutsideRoots }, statusCode: StatusCodes.Status403Forbidden);
        }

        var target = Path.Combine(parent, name);
        if (!options.TryResolve(target, out var resolved))
        {
            return Results.Json(new { error = OutsideRoots }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            Directory.CreateDirectory(resolved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new { error = "No se pudo crear la carpeta: " + e.Message });
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

        // Contar hijos puede fallar por permisos en cualquier carpeta suelta; eso no debe
        // tumbar el listado entero, solo deja la flecha de entrar sin pista previa.
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
        Results.Json(new { error = "This account is not allowed to manage libraries." },
            statusCode: StatusCodes.Status403Forbidden);
}
