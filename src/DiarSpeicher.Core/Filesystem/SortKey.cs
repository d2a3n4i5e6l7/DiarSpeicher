using System.Text;

namespace DiarSpeicher.Core.Filesystem;

/// <summary>
/// Turns a name into a key SQLite sorts the same way as <see cref="NaturalSortComparer"/>, padding
/// every numeric run to a fixed width: "Vol 2" becomes "vol 0000000002" and "Vol 10" becomes
/// "vol 0000000010", so the engine's plain text comparison already puts 2 before 10.
///
/// Sorting in memory would mean loading the whole series before paginating, and ORDER BY on the
/// raw name returns 1, 10, 11, 2. The key is stored next to the media and recomputed on rename.
/// </summary>
public static class SortKey
{
    private const int NumericWidth = 10;

    public static string From(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length + NumericWidth);
        var index = 0;

        while (index < name.Length)
        {
            if (!char.IsAsciiDigit(name[index]))
            {
                builder.Append(char.ToLowerInvariant(name[index]));
                index++;
                continue;
            }

            var start = index;
            while (index < name.Length && char.IsAsciiDigit(name[index]))
            {
                index++;
            }

            var digits = name.AsSpan(start, index - start).TrimStart('0');
            if (digits.Length > NumericWidth)
            {
                builder.Append(name.AsSpan(start, index - start));
                continue;
            }

            builder.Append('0', NumericWidth - digits.Length).Append(digits);
        }

        return builder.ToString();
    }
}
