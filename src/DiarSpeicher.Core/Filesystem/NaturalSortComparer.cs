using System.Text.RegularExpressions;

namespace DiarSpeicher.Core.Filesystem;

public class NaturalSortComparer : IComparer<string?>
{
    public static readonly NaturalSortComparer OrdinalIgnoreCase = new(StringComparison.OrdinalIgnoreCase);

    private readonly StringComparison _comparison;

    public NaturalSortComparer(StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        _comparison = comparison;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                int numCompare = CompareNumericSegments(x, ref ix, y, ref iy);
                if (numCompare != 0)
                {
                    return numCompare;
                }
            }
            else
            {
                int charCompare = string.Compare(x, ix, y, iy, 1, _comparison);
                if (charCompare != 0)
                {
                    return charCompare;
                }
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    private static int CompareNumericSegments(string x, ref int ix, string y, ref int iy)
    {
        int startX = ix;
        while (ix < x.Length && char.IsDigit(x[ix])) ix++;
        var numSpanX = x.AsSpan(startX, ix - startX);

        int startY = iy;
        while (iy < y.Length && char.IsDigit(y[iy])) iy++;
        var numSpanY = y.AsSpan(startY, iy - startY);

        var trimmedX = numSpanX.TrimStart('0');
        var trimmedY = numSpanY.TrimStart('0');

        if (trimmedX.Length != trimmedY.Length)
        {
            return trimmedX.Length.CompareTo(trimmedY.Length);
        }

        int numCompare = trimmedX.SequenceCompareTo(trimmedY);
        if (numCompare != 0)
        {
            return numCompare;
        }

        if (numSpanX.Length != numSpanY.Length)
        {
            return numSpanX.Length.CompareTo(numSpanY.Length);
        }

        return 0;
    }
}
