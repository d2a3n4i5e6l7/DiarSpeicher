using System.Text;

namespace DiarSpeicher.Core.Filesystem;

/// <summary>
/// Convierte un nombre en una clave que SQLite ordena igual que
/// <see cref="NaturalSortComparer"/>, rellenando cada tramo numerico a un ancho fijo: "Vol 2"
/// pasa a "vol 0000000002" y "Vol 10" a "vol 0000000010", de modo que la comparacion de texto
/// del motor ya coloca el 2 antes del 10.
///
/// Existe porque ordenar en memoria obligaria a traerse la serie entera antes de paginar, y
/// ORDER BY sobre el nombre crudo devuelve 1, 10, 11, 2. La clave se guarda junto al medio y
/// se recalcula al renombrar.
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
