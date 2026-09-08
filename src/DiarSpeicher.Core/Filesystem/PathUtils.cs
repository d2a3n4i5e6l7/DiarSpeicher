using System.IO;

namespace DiarSpeicher.Core.Filesystem;

public static class PathUtils
{
    public static readonly string[] AcceptedImageExtensions = ["jpg", "png", "jpeg", "jxl", "webp", "gif", "avif", "heif"];
    public static readonly string[] AcceptedMediaExtensions = ["cbz", "cbr", "zip", "rar", "epub", "pdf"];

    public static bool IsHiddenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        if (path.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
            return false;

        return fileName.StartsWith('.');
    }

    public static bool IsSupportedMedia(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var ext = Path.GetExtension(path);
        var ct = ContentTypeExtensions.FromExtension(ext);
        return !ct.IsImage() && ct.IsSupportedMedia();
    }

    public static bool IsDefaultIgnored(string path)
    {
        if (IsHiddenFile(path))
            return true;

        return !IsSupportedMedia(path);
    }

    /// <summary>
    /// Shallow check: verifies if the directory has direct supported media files (not checking subdirectories).
    /// </summary>
    public static bool DirHasMedia(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return false;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            return Directory.EnumerateFiles(directoryPath, "*", options)
                .Any(file => !IsDefaultIgnored(file));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Deep check: recursively verifies if the directory or any descendant contains supported media files.
    /// </summary>
    public static bool DirHasMediaDeep(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return false;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            return Directory.EnumerateFiles(directoryPath, "*", options)
                .Any(file => !IsDefaultIgnored(file));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
