using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class FilesystemEndpoints
{
    private static void MapBrowseRoutes(RouteGroupBuilder group)
    {
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
                : ApiResults.Error(OutsideRoots, StatusCodes.Status403Forbidden);
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
            return ApiResults.Error(OutsideRoots, StatusCodes.Status403Forbidden);
        }

        if (!Directory.Exists(resolved))
        {
            return Results.NotFound(new ApiError("La carpeta no existe o no esta montada."));
        }

        List<FolderEntryDto> entries;
        try
        {
            entries = Directory.EnumerateDirectories(resolved)
                // Hidden folders are skipped: on a manga volume they are .stfolder, .@__thumb
                // and other sync noise, never a library.
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntries)
                .Select(ToEntry)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return ApiResults.Error("Sin permiso para leer esa carpeta.", StatusCodes.Status403Forbidden);
        }
        catch (IOException)
        {
            return ApiResults.Error("No se pudo leer esa carpeta.", StatusCodes.Status500InternalServerError);
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
    /// Calls the real scanner rather than a copy of its rules: a preview that reimplements the
    /// classification starts lying as soon as someone changes the scanner.
    /// </summary>
    private static IResult Preview(
        string? path,
        string? pattern,
        LibraryRootsOptions options,
        IDirectoryScanner scanner)
    {
        if (!options.TryResolve(path, out var resolved))
        {
            return ApiResults.Error(OutsideRoots, StatusCodes.Status403Forbidden);
        }

        if (!Directory.Exists(resolved))
        {
            return Results.NotFound(new ApiError("La carpeta no existe o no esta montada."));
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

    private static DiskUsageDto ReadDisk(string path)
    {
        try
        {
            // On Unix this resolves through statvfs on the path, so a bind mount reports the
            // host file system behind it, not the container's.
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
}
