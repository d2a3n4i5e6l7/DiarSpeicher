using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

/// <param name="Regular">Cuerpo de texto.</param>
/// <param name="Bold">Titulos. Cae en la regular si el libro no trae negrita.</param>
public readonly record struct ReaderFonts(SKTypeface Regular, SKTypeface Bold);

public static class EpubFontProvider
{
    /// <summary>Valor de <c>FontFamily</c> que pide usar las fuentes incrustadas en el EPUB.</summary>
    public const string BookEmbedded = "__book__";

    private const string IdpfObfuscation = "http://www.idpf.org/2008/embedding";
    private const string AdobeObfuscation = "http://ns.adobe.com/pdf/enc#RC";

    private static readonly Dictionary<string, (string Regular, string Bold)> Bundled =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Inter"] = ("inter-400.ttf", "inter-700.ttf"),
            ["Chakra Petch"] = ("chakra-petch-400.ttf", "chakra-petch-700.ttf"),
            ["Rajdhani"] = ("rajdhani-500.ttf", "rajdhani-700.ttf"),
            ["Orbitron"] = ("orbitron-500.ttf", "orbitron-900.ttf"),
            ["JetBrains Mono"] = ("jetbrains-mono-400.ttf", "jetbrains-mono-700.ttf"),
        };

    private const string FallbackFamily = "Inter";

    // Las tipografias se reutilizan entre paginas y NO se liberan: crearlas cuesta y viven
    // lo que el proceso. Por eso los llamantes no deben envolverlas en using.
    private static readonly ConcurrentDictionary<string, SKTypeface?> Cache = new();

    private const int BookFaceLimit = 16;

    private static readonly ConcurrentDictionary<string, (SKTypeface? Face, long Used)> BookFaces = new();

    private static long _bookFaceTicks;

    private static string FontsDirectory => Path.Combine(AppContext.BaseDirectory, "Fonts");

    /// <summary>
    /// Tipografias para este perfil. Nunca devuelve null: si no hay nada utilizable acaba en
    /// la que Skia traiga por defecto, que es lo que pasaba siempre antes de que hubiera fuentes.
    /// </summary>
    public static ReaderFonts Resolve(EpubDeviceProfile profile, ZipArchive? archive)
    {
        if (string.Equals(profile.FontFamily, BookEmbedded, StringComparison.Ordinal) && archive != null)
        {
            var delLibro = FromArchive(archive);
            if (delLibro is { } f) return f;
        }

        var familia = Bundled.ContainsKey(profile.FontFamily) ? profile.FontFamily : FallbackFamily;
        var regular = LoadBundled(familia, bold: false);
        var bold = LoadBundled(familia, bold: true);

        return new ReaderFonts(regular ?? SKTypeface.Default, bold ?? regular ?? SKTypeface.Default);
    }

    private static SKTypeface? LoadBundled(string familia, bool bold)
    {
        if (!Bundled.TryGetValue(familia, out var ficheros)) return null;

        var nombre = bold ? ficheros.Bold : ficheros.Regular;
        return Cache.GetOrAdd($"bundled:{nombre}", _ =>
        {
            var ruta = Path.Combine(FontsDirectory, nombre);
            return File.Exists(ruta) ? SKTypeface.FromFile(ruta) : null;
        });
    }

    // ---------------------------------------------------------------- libro

    private static ReaderFonts? FromArchive(ZipArchive archive)
    {
        var reglas = FontFaceRules(archive);
        if (reglas.Count == 0) return null;

        var uid = UniqueIdentifier(archive);
        var cifrados = ObfuscatedEntries(archive);

        var regular = PickFace(archive, reglas, uid, cifrados, bold: false);
        if (regular == null) return null;

        var bold = PickFace(archive, reglas, uid, cifrados, bold: true) ?? regular;
        return new ReaderFonts(regular, bold);
    }

    private static SKTypeface? PickFace(
        ZipArchive archive,
        List<(string Path, int Weight, bool Italic)> reglas,
        string uid,
        IReadOnlyDictionary<string, string> cifrados,
        bool bold)
    {
        var objetivo = bold ? 700 : 400;
        var elegida = reglas
            .Where(r => !r.Italic)
            .OrderBy(r => Math.Abs(r.Weight - objetivo))
            .FirstOrDefault();

        if (elegida.Path == null) return null;
        // Sin negrita propia no se sustituye por la regular: eso lo decide el llamante.
        if (bold && Math.Abs(elegida.Weight - objetivo) > 200) return null;

        var bytes = LoadFaceBytes(archive, elegida.Path, uid, cifrados);
        if (bytes.Length == 0) return null;

        var clave = Convert.ToHexString(SHA256.HashData(bytes));
        if (BookFaces.TryGetValue(clave, out var guardada))
        {
            BookFaces[clave] = (guardada.Face, Interlocked.Increment(ref _bookFaceTicks));
            return guardada.Face;
        }

        var creada = SKTypeface.FromStream(new MemoryStream(bytes));
        TrimBookFaces();
        BookFaces[clave] = (creada, Interlocked.Increment(ref _bookFaceTicks));
        return creada;
    }

    private static byte[] LoadFaceBytes(
        ZipArchive archive,
        string ruta,
        string uid,
        IReadOnlyDictionary<string, string> cifrados)
    {
        var entrada = archive.GetEntry(ruta);
        if (entrada == null) return [];

        using var ms = new MemoryStream(entrada.Length is > 0 and < int.MaxValue ? (int)entrada.Length : 0);
        using (var s = entrada.Open()) s.CopyTo(ms);
        var bytes = ms.ToArray();

        return cifrados.TryGetValue(ruta, out var algoritmo)
            ? Deobfuscate(bytes, uid, algoritmo)
            : bytes;
    }

    private static void TrimBookFaces()
    {
        while (BookFaces.Count >= BookFaceLimit)
        {
            var vieja = BookFaces.OrderBy(e => e.Value.Used).Select(e => e.Key).FirstOrDefault();
            if (vieja == null || !BookFaces.TryRemove(vieja, out _)) return;
        }
    }

    private static readonly Regex FontFaceRegex =
        new(@"@font-face\s*\{(.*?)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlRegex =
        new(@"url\(\s*['""]?([^'""\)]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static List<(string Path, int Weight, bool Italic)> FontFaceRules(ZipArchive archive)
    {
        var fuera = new List<(string, int, bool)>();

        foreach (var hoja in archive.Entries.Where(e => e.FullName.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
        {
            string css;
            using (var reader = new StreamReader(hoja.Open())) css = reader.ReadToEnd();

            var dir = Path.GetDirectoryName(hoja.FullName)?.Replace('\\', '/') ?? "";

            foreach (Match m in FontFaceRegex.Matches(css))
            {
                var bloque = m.Groups[1].Value;
                var url = UrlRegex.Match(bloque);
                if (!url.Success) continue;

                var ruta = ResolveRelative(dir, url.Groups[1].Value.Trim());
                if (archive.GetEntry(ruta) == null) continue;

                fuera.Add((ruta, ParseWeight(Declaration(bloque, "font-weight")),
                    (Declaration(bloque, "font-style") ?? "").Contains("italic", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return fuera;
    }

    private static string? Declaration(string bloque, string nombre)
    {
        var m = Regex.Match(bloque, $@"{nombre}\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static int ParseWeight(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        null or "" or "normal" => 400,
        "bold" => 700,
        "lighter" => 300,
        "bolder" => 700,
        var v when int.TryParse(v, out var n) => n,
        _ => 400,
    };

    private static string ResolveRelative(string dir, string href)
    {
        var partes = $"{dir}/{href}".Split('/');
        var pila = new List<string>();
        foreach (var p in partes)
        {
            if (p == "..") { if (pila.Count > 0) pila.RemoveAt(pila.Count - 1); }
            else if (p is not ("." or "")) pila.Add(p);
        }
        return string.Join('/', pila);
    }

    // ------------------------------------------------------------ ofuscado

    private static string UniqueIdentifier(ZipArchive archive)
    {
        try
        {
            var container = archive.GetEntry("META-INF/container.xml");
            if (container == null) return "";

            XNamespace cn = "urn:oasis:names:tc:opendocument:xmlns:container";
            XDocument contenedor;
            using (var s = container.Open()) contenedor = XDocument.Load(s);
            var rootfile = contenedor.Descendants(cn + "rootfile").FirstOrDefault();
            var opfPath = rootfile?.Attribute("full-path")?.Value;
            if (opfPath == null) return "";

            var opf = archive.GetEntry(opfPath);
            if (opf == null) return "";

            XNamespace on = "http://www.idpf.org/2007/opf";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XDocument doc;
            using (var s = opf.Open()) doc = XDocument.Load(s);
            var idref = doc.Root?.Attribute("unique-identifier")?.Value;

            var ids = doc.Descendants(dc + "identifier").ToList();
            var elegido = ids.FirstOrDefault(i => i.Attribute("id")?.Value == idref) ?? ids.FirstOrDefault();
            return elegido?.Value.Trim() ?? "";
        }
        catch
        {
            // Un OPF ilegible solo significa que no se podran desofuscar fuentes; el libro
            // se sigue sirviendo con las nuestras.
            return "";
        }
    }

    private static IReadOnlyDictionary<string, string> ObfuscatedEntries(ZipArchive archive)
    {
        var fuera = new Dictionary<string, string>(StringComparer.Ordinal);
        var enc = archive.GetEntry("META-INF/encryption.xml");
        if (enc == null) return fuera;

        try
        {
            XNamespace e = "http://www.w3.org/2001/04/xmlenc#";
            XDocument cifrado;
            using (var s = enc.Open()) cifrado = XDocument.Load(s);

            foreach (var datos in cifrado.Descendants(e + "EncryptedData"))
            {
                var alg = datos.Descendants(e + "EncryptionMethod").FirstOrDefault()?.Attribute("Algorithm")?.Value;
                var uri = datos.Descendants(e + "CipherReference").FirstOrDefault()?.Attribute("URI")?.Value;
                if (alg != null && uri != null) fuera[Uri.UnescapeDataString(uri.TrimStart('/'))] = alg;
            }
        }
        catch
        {
            return fuera;
        }

        return fuera;
    }

    /// <summary>
    /// Deshace el enmascarado de fuentes. No es DRM: el algoritmo es publico y existe para
    /// poner freno a quien no ha leido la licencia tipografica, no para cifrar el libro.
    /// </summary>
    private static byte[] Deobfuscate(byte[] datos, string uid, string algoritmo)
    {
        byte[] clave;
        int cuantos;

        if (algoritmo == IdpfObfuscation)
        {
            // La spec: quitar los espacios XML del identificador, luego SHA-1.
            var limpio = new string(uid.Where(c => c is not (' ' or '\t' or '\r' or '\n')).ToArray());
            clave = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(limpio));
            cuantos = 1040;
        }
        else if (algoritmo == AdobeObfuscation)
        {
            var hex = uid.Replace("urn:uuid:", "", StringComparison.OrdinalIgnoreCase).Replace("-", "");
            try { clave = Convert.FromHexString(hex); }
            catch { return datos; }
            cuantos = 1024;
        }
        else
        {
            // Cifrado de verdad (DRM): no se toca.
            return [];
        }

        if (clave.Length == 0) return datos;

        var fuera = (byte[])datos.Clone();
        var tope = Math.Min(cuantos, fuera.Length);
        for (var i = 0; i < tope; i++) fuera[i] ^= clave[i % clave.Length];
        return fuera;
    }
}
