using System.Collections.Frozen;

namespace DiarSpeicher.Core.Filesystem;

public enum ContentType
{
    Unknown,
    XHtml,
    Xml,
    Html,
    Pdf,
    EpubZip,
    Zip,
    ComicZip,
    Rar,
    ComicRar,
    Avif,
    Heif,
    Png,
    Jpeg,
    JpegXl,
    Webp,
    Gif,
    Txt
}

public static class ContentTypeExtensions
{
    private static readonly FrozenDictionary<string, ContentType> ExtensionMap =
        new Dictionary<string, ContentType>(StringComparer.OrdinalIgnoreCase)
        {
            ["xhtml"] = ContentType.XHtml,
            ["xml"] = ContentType.Xml,
            ["opf"] = ContentType.Xml,
            ["ncx"] = ContentType.Xml,
            ["html"] = ContentType.Html,
            ["htm"] = ContentType.Html,
            ["pdf"] = ContentType.Pdf,
            ["epub"] = ContentType.EpubZip,
            ["zip"] = ContentType.Zip,
            ["cbz"] = ContentType.ComicZip,
            ["rar"] = ContentType.Rar,
            ["cbr"] = ContentType.ComicRar,
            ["avif"] = ContentType.Avif,
            ["heif"] = ContentType.Heif,
            ["png"] = ContentType.Png,
            ["jpg"] = ContentType.Jpeg,
            ["jpeg"] = ContentType.Jpeg,
            ["jxl"] = ContentType.JpegXl,
            ["webp"] = ContentType.Webp,
            ["gif"] = ContentType.Gif,
            ["txt"] = ContentType.Txt,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<ContentType, string> MimeTypeMap =
        new Dictionary<ContentType, string>
        {
            [ContentType.XHtml] = "application/xhtml+xml",
            [ContentType.Xml] = "application/xml",
            [ContentType.Html] = "text/html",
            [ContentType.Pdf] = "application/pdf",
            [ContentType.EpubZip] = "application/epub+zip",
            [ContentType.Zip] = "application/zip",
            [ContentType.ComicZip] = "application/vnd.comicbook+zip",
            [ContentType.Rar] = "application/vnd.rar",
            [ContentType.ComicRar] = "application/vnd.comicbook-rar",
            [ContentType.Avif] = "image/avif",
            [ContentType.Heif] = "image/heif",
            [ContentType.Png] = "image/png",
            [ContentType.Jpeg] = "image/jpeg",
            [ContentType.JpegXl] = "image/jxl",
            [ContentType.Webp] = "image/webp",
            [ContentType.Gif] = "image/gif",
            [ContentType.Txt] = "text/plain",
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<ContentType, string> DefaultExtensionMap =
        new Dictionary<ContentType, string>
        {
            [ContentType.XHtml] = "xhtml",
            [ContentType.Xml] = "xml",
            [ContentType.Html] = "html",
            [ContentType.Pdf] = "pdf",
            [ContentType.EpubZip] = "epub",
            [ContentType.Zip] = "zip",
            [ContentType.ComicZip] = "cbz",
            [ContentType.Rar] = "rar",
            [ContentType.ComicRar] = "cbr",
            [ContentType.Avif] = "avif",
            [ContentType.Heif] = "heif",
            [ContentType.Png] = "png",
            [ContentType.Jpeg] = "jpg",
            [ContentType.JpegXl] = "jxl",
            [ContentType.Webp] = "webp",
            [ContentType.Gif] = "gif",
            [ContentType.Txt] = "txt",
        }.ToFrozenDictionary();

    public static ContentType FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ContentType.Unknown;
        }

        var cleanExt = extension.TrimStart('.');
        return ExtensionMap.TryGetValue(cleanExt, out var type) ? type : ContentType.Unknown;
    }

    public static string MimeType(this ContentType contentType) => contentType.ToMimeType();

    public static string ToMimeType(this ContentType contentType) =>
        MimeTypeMap.TryGetValue(contentType, out var mime) ? mime : "application/octet-stream";

    public static string DefaultExtension(this ContentType contentType) =>
        DefaultExtensionMap.TryGetValue(contentType, out var ext) ? ext : string.Empty;

    public static bool IsImage(this ContentType contentType) =>
        contentType is >= ContentType.Avif and <= ContentType.Gif;

    public static bool IsZip(this ContentType contentType) =>
        contentType is ContentType.Zip or ContentType.ComicZip;

    public static bool IsRar(this ContentType contentType) =>
        contentType is ContentType.Rar or ContentType.ComicRar;

    public static bool IsEpub(this ContentType contentType) =>
        contentType is ContentType.EpubZip;

    public static bool IsSupportedMedia(this ContentType contentType) =>
        contentType is >= ContentType.Pdf and <= ContentType.ComicRar;
}
