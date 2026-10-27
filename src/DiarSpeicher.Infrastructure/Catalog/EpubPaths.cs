namespace DiarSpeicher.Infrastructure.Catalog;

internal static class EpubPaths
{
    private static readonly Uri ArchiveRoot = new("epub://archive/");

    public static string DirectoryOf(string entryPath)
    {
        var slash = entryPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : entryPath[..(slash + 1)];
    }

    public static (string Path, string? Fragment) Resolve(string directory, string href)
    {
        var target = new Uri(new Uri(ArchiveRoot, directory), href);
        var fragment = target.Fragment.Length > 1 ? Uri.UnescapeDataString(target.Fragment[1..]) : null;
        return (Uri.UnescapeDataString(target.AbsolutePath.TrimStart('/')), fragment);
    }

    public static string Escape(string entryPath) =>
        string.Join('/', entryPath.Split('/').Select(Uri.EscapeDataString));
}
