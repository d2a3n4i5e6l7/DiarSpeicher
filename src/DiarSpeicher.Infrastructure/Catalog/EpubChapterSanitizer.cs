using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace DiarSpeicher.Infrastructure.Catalog;

internal static class EpubChapterSanitizer
{
    public const string AnchorPrefix = "ep-";

    private const string RemovedElements = "script, style, link, meta, base, iframe, frame, object, embed, form, input, button, textarea, select, noscript";

    private static readonly string[] DroppedAttributes = ["style", "class", "srcset"];

    private static readonly string[] ImageSourceAttributes = ["src", "href", "xlink:href"];

    private static readonly string[] ExternalLinkSchemes = ["http:", "https:", "mailto:"];

    private static readonly IHtmlParser Parser = new HtmlParser();

    public static string Sanitize(string html, string chapterPath, string resourceBase, string version)
    {
        using var document = Parser.ParseDocument(html);
        var body = document.Body;
        if (body is null) return string.Empty;

        foreach (var element in body.QuerySelectorAll(RemovedElements).ToList())
        {
            element.Remove();
        }

        var directory = EpubPaths.DirectoryOf(chapterPath);
        foreach (var element in body.QuerySelectorAll("*"))
        {
            StripAttributes(element);
            switch (element.LocalName)
            {
                case "img" or "image":
                    RewriteImageSource(element, directory, resourceBase, version);
                    break;
                case "a":
                    RewriteLink(element);
                    break;
            }
        }

        return body.InnerHtml;
    }

    private static void StripAttributes(IElement element)
    {
        var names = element.Attributes.Select(attribute => attribute.Name).ToList();
        foreach (var name in names)
        {
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || DroppedAttributes.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                element.RemoveAttribute(name);
            }
        }

        if (element.Id is { Length: > 0 } id)
        {
            element.Id = AnchorPrefix + id;
        }
    }

    private static void RewriteImageSource(IElement element, string directory, string resourceBase, string version)
    {
        foreach (var name in ImageSourceAttributes)
        {
            var value = element.GetAttribute(name);
            if (value is null || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            element.RemoveAttribute(name);
            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && !absolute.IsFile) continue;

            var (path, _) = EpubPaths.Resolve(directory, value);
            element.SetAttribute(element.LocalName == "img" ? "src" : "href", $"{resourceBase}{EpubPaths.Escape(path)}?v={version}");
        }
    }

    private static void RewriteLink(IElement element)
    {
        var href = element.GetAttribute("href");
        if (href is not null && ExternalLinkSchemes.Any(scheme => href.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
        {
            element.SetAttribute("target", "_blank");
            element.SetAttribute("rel", "noopener noreferrer");
            return;
        }

        element.RemoveAttribute("href");
    }
}
