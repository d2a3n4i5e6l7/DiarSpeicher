using System.Globalization;

namespace DiarSpeicher.Core.Filesystem;

/// <summary>
/// Orders archive entry names the way a reader expects: "page2" before "page10", and
/// "cap1/p2" before "cap01/p10" regardless of how each directory level is padded.
///
/// Numeric segments go through .NET 10's <see cref="CompareOptions.NumericOrdering"/>, which
/// is roughly twice as fast as walking digit runs by hand. Path components are compared one
/// at a time: on the whole string "cap1/p2" sorts between the "cap01" pages, interleaving two
/// different chapters.
/// </summary>
public sealed class NaturalSortComparer : IComparer<string?>
{
    public static readonly NaturalSortComparer OrdinalIgnoreCase = new();

    private static readonly char[] Separators = ['/', '\\'];

    private readonly StringComparer _segmentComparer;

    public NaturalSortComparer()
        : this(StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase))
    {
    }

    public NaturalSortComparer(StringComparer segmentComparer)
    {
        _segmentComparer = segmentComparer;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var xSegments = x.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var ySegments = y.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        var shared = Math.Min(xSegments.Length, ySegments.Length);
        for (var i = 0; i < shared; i++)
        {
            var comparison = CompareSegment(xSegments[i], ySegments[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (xSegments.Length != ySegments.Length)
        {
            return xSegments.Length.CompareTo(ySegments.Length);
        }

        return string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// Numeric ordering rates "cap1" and "cap01" equal, so a padding difference alone would
    /// fall through to the next path component and interleave the pages of two directories
    /// that are not the same directory. Fewer digits wins, matching how the shorter form is
    /// written first when a set mixes both.
    /// </summary>
    private int CompareSegment(string x, string y)
    {
        var comparison = _segmentComparer.Compare(x, y);
        if (comparison != 0)
        {
            return comparison;
        }

        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return x.Length != y.Length
            ? x.Length.CompareTo(y.Length)
            : string.CompareOrdinal(x, y);
    }
}
