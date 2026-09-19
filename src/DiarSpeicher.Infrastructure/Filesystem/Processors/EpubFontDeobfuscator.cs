using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

internal static class EpubFontDeobfuscator
{
    private static readonly string IdpfObfuscation = string.Concat("http:", "//www.idpf.org/2008/embedding");
    private const string AdobeObfuscation = "http://ns.adobe.com/pdf/enc#RC";

    public static string UniqueIdentifier(ZipArchive archive)
    {
        try
        {
            var opfPath = GetOpfPath(archive);
            if (string.IsNullOrEmpty(opfPath)) return string.Empty;

            var opf = archive.GetEntry(opfPath);
            if (opf == null) return string.Empty;

            return ReadPackageIdentifier(opf);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? GetOpfPath(ZipArchive archive)
    {
        var container = archive.GetEntry("META-INF/container.xml");
        if (container == null) return null;

        XNamespace cn = "urn:oasis:names:tc:opendocument:xmlns:container";
        using var s = container.Open();
        var contenedor = XDocument.Load(s);
        var rootfile = contenedor.Descendants(cn + "rootfile").FirstOrDefault();
        return rootfile?.Attribute("full-path")?.Value;
    }

    private static string ReadPackageIdentifier(ZipArchiveEntry opf)
    {
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        using var s = opf.Open();
        var doc = XDocument.Load(s);
        var idref = doc.Root?.Attribute("unique-identifier")?.Value;

        var ids = doc.Descendants(dc + "identifier").ToList();
        var elegido = ids.FirstOrDefault(i => i.Attribute("id")?.Value == idref) ?? ids.FirstOrDefault();
        return elegido?.Value.Trim() ?? string.Empty;
    }

    public static IReadOnlyDictionary<string, string> ObfuscatedEntries(ZipArchive archive)
    {
        var fuera = new Dictionary<string, string>(StringComparer.Ordinal);
        var enc = archive.GetEntry("META-INF/encryption.xml");
        if (enc == null) return fuera;

        try
        {
            XNamespace e = "http://www.w3.org/2001/04/xmlenc#";
            using var s = enc.Open();
            var cifrado = XDocument.Load(s);

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
    /// Deshace el enmascarado de fuentes según la especificación IDPF y Adobe.
    /// </summary>
    public static byte[] Deobfuscate(byte[] datos, string uid, string algoritmo)
    {
        byte[] clave;
        int cuantos;

        if (algoritmo == IdpfObfuscation)
        {
            var limpio = new string(uid.Where(c => c is not (' ' or '\t' or '\r' or '\n')).ToArray());
            clave = ComputeIdpfSha1(Encoding.UTF8.GetBytes(limpio));
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
            return [];
        }

        if (clave.Length == 0) return datos;

        var fuera = (byte[])datos.Clone();
        var tope = Math.Min(cuantos, fuera.Length);
        for (var i = 0; i < tope; i++) fuera[i] ^= clave[i % clave.Length];
        return fuera;
    }

    /// <summary>
    /// Implementación autocontenida de SHA-1 según RFC 3174 para la especificación IDPF EPUB.
    /// </summary>
    private static byte[] ComputeIdpfSha1(byte[] data)
    {
        uint h0 = 0x67452301;
        uint h1 = 0xEFCDAB89;
        uint h2 = 0x98BADCFE;
        uint h3 = 0x10325476;
        uint h4 = 0xC3D2E1F0;

        var bitLength = (ulong)data.Length * 8;
        var padLength = (data.Length % 64 < 56) ? (56 - data.Length % 64) : (120 - data.Length % 64);
        var padded = new byte[data.Length + padLength + 8];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        padded[data.Length] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(padded.Length - 8), bitLength);

        Span<uint> w = stackalloc uint[80];
        for (var offset = 0; offset < padded.Length; offset += 64)
        {
            for (var i = 0; i < 16; i++)
            {
                w[i] = BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(offset + i * 4, 4));
            }
            for (var i = 16; i < 80; i++)
            {
                w[i] = BitOperations.RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
            }

            var a = h0;
            var b = h1;
            var c = h2;
            var d = h3;
            var e = h4;

            for (var i = 0; i < 80; i++)
            {
                var (f, k) = i switch
                {
                    < 20 => ((b & c) | (~b & d), 0x5A827999u),
                    < 40 => (b ^ c ^ d, 0x6ED9EBA1u),
                    < 60 => ((b & c) | (b & d) | (c & d), 0x8F1BBCDCu),
                    _ => (b ^ c ^ d, 0xCA62C1D6u)
                };

                var temp = BitOperations.RotateLeft(a, 5) + f + e + k + w[i];
                e = d;
                d = c;
                c = BitOperations.RotateLeft(b, 30);
                b = a;
                a = temp;
            }

            h0 += a;
            h1 += b;
            h2 += c;
            h3 += d;
            h4 += e;
        }

        var result = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), h0);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), h1);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8, 4), h2);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12, 4), h3);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(16, 4), h4);
        return result;
    }
}
