using System.Text;
using System.Text.Json;
using DiarSpeicher.Api.Serialization;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class TusEndpoints
{
    private static Task<IResult> HandleDelete(
        string id,
        HttpContext ctx,
        [FromServices] IOptions<StorageOptions> storageOptions)
    {
        AddTusHeaders(ctx);

        var user = ctx.Items[AuthUserKey] as AuthUser;
        if (user == null) return Task.FromResult<IResult>(Results.Unauthorized());

        var uploadsDir = storageOptions.Value.ResolveUploadsPath();
        var metaPath = Path.Combine(uploadsDir, $"{id}.meta");

        if (File.Exists(metaPath))
        {
            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize(metaJson, PersistedJsonContext.Default.TusUploadMeta);
                if (meta != null && !string.IsNullOrEmpty(meta.PartPath) && File.Exists(meta.PartPath))
                {
                    File.Delete(meta.PartPath);
                }
            }
            catch
            {
                // Best effort: the meta file below is removed even when the part file cannot
                // be.
            }
            File.Delete(metaPath);
        }

        var legacyPart = Path.Combine(uploadsDir, $"{id}.part");
        if (File.Exists(legacyPart)) File.Delete(legacyPart);

        return Task.FromResult<IResult>(Results.NoContent());
    }

    private static void AddTusHeaders(HttpContext ctx)
    {
        ctx.Response.Headers.Append("Tus-Resumable", TusResumableVersion);
    }

    private static Dictionary<string, string> ParseUploadMetadata(string? rawMetadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawMetadata)) return result;

        var pairs = rawMetadata.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;

            var key = parts[0];
            var value = "";
            if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
            {
                try
                {
                    var bytes = Convert.FromBase64String(parts[1]);
                    value = Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    value = parts[1];
                }
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Creates the directory if it is missing and writes a throwaway probe file to it.
    /// Directory.Exists says nothing about whether writes are accepted: a read-only bind
    /// mount reports every directory in it as existing.
    /// </summary>
    private static bool TryEnsureWritableDirectory(string directory, out string error)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var probePath = Path.Combine(directory, $".diarspeicher-write-probe-{Guid.NewGuid():N}");
            using (var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                probe.WriteByte(0);
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Best-effort cleanup: must not mask the error that triggered it.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked file or no permission: the temp file stays, which is better than hiding
            // the error that triggered the cleanup.
        }
    }

    private static bool _uploadOptionsAllows(UploadOptions options, string fileName)
    {
        return options.IsExtensionAllowed(fileName);
    }

    private static bool TryResolveTargetDirectory(string libraryPath, string? subpath, out string fullTargetDir)
    {
        var cleanSubpath = (subpath ?? string.Empty).Trim('/', '\\');
        var targetDir = string.IsNullOrEmpty(cleanSubpath)
            ? libraryPath
            : Path.Combine(libraryPath, cleanSubpath);

        fullTargetDir = Path.GetFullPath(targetDir);
        var fullLibraryPath = Path.GetFullPath(libraryPath);

        var libraryPrefix = fullLibraryPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullLibraryPath
            : fullLibraryPath + Path.DirectorySeparatorChar;

        return fullTargetDir.Equals(fullLibraryPath, StringComparison.OrdinalIgnoreCase) ||
               fullTargetDir.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
