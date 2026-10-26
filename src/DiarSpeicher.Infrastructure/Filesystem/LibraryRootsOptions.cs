namespace DiarSpeicher.Infrastructure.Filesystem;

/// <summary>
/// Without it the folder explorer lets anyone with <c>ManageLibrary</c> read the whole file
/// system of the container.
/// </summary>
public class LibraryRootsOptions
{
    public const string SectionName = "Libraries";

    public const string DefaultRoot = "/libraries";

    /// <summary>
    /// Empty on purpose: configuration binding <em>appends</em> to the list instead of
    /// replacing it, so a default here would come out duplicated.
    /// </summary>
    public List<string> Roots { get; set; } = [];

    /// <summary>
    /// Includes roots that do not exist on disk, so "not configured" can be told apart from
    /// "configured but not mounted".
    /// </summary>
    public IReadOnlyList<string> ResolveRoots()
    {
        var declared = Roots.Count > 0 ? Roots : [DefaultRoot];

        return declared
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The path is resolved before checking the prefix: with the original string a <c>..</c>
    /// slips through.
    /// </summary>
    public bool TryResolve(string? candidate, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        string full;
        try
        {
            full = Normalize(candidate);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (ResolveRoots().Any(root => IsInside(full, root)))
        {
            resolved = full;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A root is the container of libraries, not a library. Registering it as one leaves an
    /// entry that can never be removed from disk, because deleting the folder would take every
    /// other library under it along.
    /// </summary>
    public bool IsRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        string full;
        try
        {
            full = Normalize(candidate);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return ResolveRoots().Any(root => string.Equals(full, root, comparison));
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    /// <summary>
    /// The trailing separator is mandatory: without it "/libraries-private" would pass as being
    /// inside "/libraries".
    /// </summary>
    private static bool IsInside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(path, root, comparison)) return true;

        return path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
