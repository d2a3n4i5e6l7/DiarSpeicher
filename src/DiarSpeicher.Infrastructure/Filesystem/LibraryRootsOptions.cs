namespace DiarSpeicher.Infrastructure.Filesystem;

/// <summary>
/// Lista blanca de carpetas donde pueden vivir bibliotecas. Seccion "Libraries".
/// <para>
/// Sin ella, el explorador de carpetas deja leer el sistema de ficheros entero del
/// contenedor a cualquiera con <c>ManageLibrary</c>.
/// </para>
/// <para>
/// Cada disco montado en el compose se declara aqui:
/// <c>Libraries__Roots__0=/libraries/manga</c>, <c>Libraries__Roots__1=/libraries/comics</c>.
/// </para>
/// </summary>
public class LibraryRootsOptions
{
    public const string SectionName = "Libraries";

    public const string DefaultRoot = "/libraries";

    /// <summary>
    /// Vacia a proposito: el enlace de configuracion <em>anade</em> a la lista en vez de
    /// reemplazarla, asi que un valor por defecto aqui saldria duplicado.
    /// </summary>
    public List<string> Roots { get; set; } = [];

    /// <summary>
    /// Incluye las raices que no existen en disco, para poder distinguir "no configurada"
    /// de "configurada pero sin montar".
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
    /// Resuelve la ruta antes de comprobar el prefijo: con la cadena original, un
    /// <c>..</c> se cuela.
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

        foreach (var root in ResolveRoots())
        {
            if (IsInside(full, root))
            {
                resolved = full;
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    /// <summary>
    /// El separador final es obligatorio: sin el, "/libraries-privadas" pasaria por estar
    /// dentro de "/libraries".
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
