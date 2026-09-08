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
    public static ContentType FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ContentType.Unknown;
        }

        var cleanExt = extension.TrimStart('.').ToLowerInvariant();
        return cleanExt switch
        {
            "xhtml" => ContentType.XHtml,
            "xml" or "opf" or "ncx" => ContentType.Xml,
            "html" or "htm" => ContentType.Html,
            "pdf" => ContentType.Pdf,
            "epub" => ContentType.EpubZip,
            "zip" => ContentType.Zip,
            "cbz" => ContentType.ComicZip,
            "rar" => ContentType.Rar,
            "cbr" => ContentType.ComicRar,
            "avif" => ContentType.Avif,
            "heif" => ContentType.Heif,
            "png" => ContentType.Png,
            "jpg" or "jpeg" => ContentType.Jpeg,
            "jxl" => ContentType.JpegXl,
            "webp" => ContentType.Webp,
            "gif" => ContentType.Gif,
            "txt" => ContentType.Txt,
            _ => ContentType.Unknown
        };
    }

    public static string MimeType(this ContentType contentType) => contentType.ToMimeType();

    public static string ToMimeType(this ContentType contentType) => contentType switch
    {
        ContentType.XHtml => "application/xhtml+xml",
        ContentType.Xml => "application/xml",
        ContentType.Html => "text/html",
        ContentType.Pdf => "application/pdf",
        ContentType.EpubZip => "application/epub+zip",
        ContentType.Zip => "application/zip",
        ContentType.ComicZip => "application/vnd.comicbook+zip",
        ContentType.Rar => "application/vnd.rar",
        ContentType.ComicRar => "application/vnd.comicbook-rar",
        ContentType.Avif => "image/avif",
        ContentType.Heif => "image/heif",
        ContentType.Png => "image/png",
        ContentType.Jpeg => "image/jpeg",
        ContentType.JpegXl => "image/jxl",
        ContentType.Webp => "image/webp",
        ContentType.Gif => "image/gif",
        ContentType.Txt => "text/plain",
        _ => "application/octet-stream"
    };

    public static string DefaultExtension(this ContentType contentType) => contentType switch
    {
        ContentType.XHtml => "xhtml",
        ContentType.Xml => "xml",
        ContentType.Html => "html",
        ContentType.Pdf => "pdf",
        ContentType.EpubZip => "epub",
        ContentType.Zip => "zip",
        ContentType.ComicZip => "cbz",
        ContentType.Rar => "rar",
        ContentType.ComicRar => "cbr",
        ContentType.Avif => "avif",
        ContentType.Heif => "heif",
        ContentType.Png => "png",
        ContentType.Jpeg => "jpg",
        ContentType.JpegXl => "jxl",
        ContentType.Webp => "webp",
        ContentType.Gif => "gif",
        ContentType.Txt => "txt",
        _ => string.Empty
    };

    public static bool IsImage(this ContentType contentType) => contentType switch
    {
        ContentType.Avif or ContentType.Heif or ContentType.Png or ContentType.Jpeg
            or ContentType.JpegXl or ContentType.Webp or ContentType.Gif => true,
        _ => false
    };

    public static bool IsZip(this ContentType contentType) =>
        contentType is ContentType.Zip or ContentType.ComicZip;

    public static bool IsRar(this ContentType contentType) =>
        contentType is ContentType.Rar or ContentType.ComicRar;

    public static bool IsEpub(this ContentType contentType) =>
        contentType is ContentType.EpubZip;

    public static bool IsSupportedMedia(this ContentType contentType) =>
        contentType is ContentType.ComicZip or ContentType.ComicRar or ContentType.EpubZip
            or ContentType.Pdf or ContentType.Zip or ContentType.Rar;
}
