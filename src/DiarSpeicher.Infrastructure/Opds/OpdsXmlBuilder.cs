using System.Xml;
using System.Xml.Linq;
using DiarSpeicher.Core.Domain.Opds;

namespace DiarSpeicher.Infrastructure.Opds;

public static class OpdsXmlBuilder
{
    private const string Utf8 = "UTF-8";
    private static readonly XNamespace AtomNs = OpdsNamespaces.Atom;
    private static readonly XNamespace OpdsNs = OpdsNamespaces.Opds;
    private static readonly XNamespace PseNs = OpdsNamespaces.Pse;
    private static readonly XNamespace OpenSearchNs = OpdsNamespaces.OpenSearch;

    public static string BuildFeedXml(OpdsFeed feed)
    {
        var feedElem = new XElement(AtomNs + "feed",
            new XAttribute("xmlns", AtomNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "opds", OpdsNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pse", PseNs.NamespaceName),
            new XElement(AtomNs + "id", feed.Id),
            new XElement(AtomNs + "title", feed.Title),
            new XElement(AtomNs + "updated", feed.Updated.ToString("yyyy-MM-dd'T'HH:mm:sszzz"))
        );

        if (feed.Author != null)
        {
            var authorElem = new XElement(AtomNs + "author",
                new XElement(AtomNs + "name", feed.Author.Name)
            );
            if (!string.IsNullOrWhiteSpace(feed.Author.Uri))
            {
                authorElem.Add(new XElement(AtomNs + "uri", feed.Author.Uri));
            }
            feedElem.Add(authorElem);
        }

        foreach (var link in feed.Links)
        {
            feedElem.Add(new XElement(AtomNs + "link",
                new XAttribute("type", link.Type),
                new XAttribute("rel", link.Rel),
                new XAttribute("href", link.Href)
            ));
        }

        foreach (var entry in feed.Entries)
        {
            feedElem.Add(BuildEntryElement(entry));
        }

        var doc = new XDocument(new XDeclaration("1.0", Utf8, null), feedElem);
        using var sw = new StringWriter();
        using (var xw = XmlWriter.Create(sw, new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Indent = true
        }))
        {
            doc.Save(xw);
        }
        return sw.ToString();
    }

    private static XElement BuildEntryElement(OpdsEntry entry)
    {
        var entryElem = new XElement(AtomNs + "entry",
            new XElement(AtomNs + "title", entry.Title),
            new XElement(AtomNs + "id", entry.Id),
            new XElement(AtomNs + "updated", entry.Updated.ToString("yyyy-MM-dd'T'HH:mm:sszzz"))
        );

        if (!string.IsNullOrWhiteSpace(entry.Summary))
        {
            entryElem.Add(new XElement(AtomNs + "summary", entry.Summary));
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            entryElem.Add(new XElement(AtomNs + "content",
                new XAttribute("type", "html"),
                entry.Content));
        }
        else
        {
            entryElem.Add(new XElement(AtomNs + "content"));
        }

        if (entry.Authors != null && entry.Authors.Count > 0)
        {
            var authorElem = new XElement(AtomNs + "author");
            foreach (var author in entry.Authors)
            {
                authorElem.Add(new XElement(AtomNs + "name", author));
            }
            entryElem.Add(authorElem);
        }

        foreach (var link in entry.Links)
        {
            entryElem.Add(new XElement(AtomNs + "link",
                new XAttribute("type", link.Type),
                new XAttribute("rel", link.Rel),
                new XAttribute("href", link.Href)
            ));
        }

        if (entry.StreamLink != null)
        {
            entryElem.Add(BuildStreamLinkElement(entry.StreamLink));
        }

        return entryElem;
    }

    private static XElement BuildStreamLinkElement(OpdsStreamLink streamLink)
    {
        var streamElem = new XElement(AtomNs + "link",
            new XAttribute("href", streamLink.Href),
            new XAttribute("type", streamLink.MimeType),
            new XAttribute("rel", OpdsLinkRel.PageStream),
            new XAttribute(PseNs + "count", streamLink.Count.ToString())
        );

        if (streamLink.LastRead.HasValue)
        {
            streamElem.Add(new XAttribute(PseNs + "lastRead", streamLink.LastRead.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(streamLink.LastReadDate))
        {
            streamElem.Add(new XAttribute(PseNs + "lastReadDate", streamLink.LastReadDate));
        }

        return streamElem;
    }

    public static string BuildOpenSearchXml(string searchUrl)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", Utf8, null),
            new XElement(OpenSearchNs + "OpenSearchDescription",
                new XAttribute("xmlns", OpenSearchNs.NamespaceName),
                new XElement(OpenSearchNs + "ShortName", "Search"),
                new XElement(OpenSearchNs + "Description", "Search by keyword"),
                new XElement(OpenSearchNs + "InputEncoding", Utf8),
                new XElement(OpenSearchNs + "OutputEncoding", Utf8),
                new XElement(OpenSearchNs + "Url",
                    new XAttribute("template", searchUrl),
                    new XAttribute("type", OpdsLinkType.Acquisition)
                )
            )
        );

        using var sw = new StringWriter();
        using (var xw = XmlWriter.Create(sw, new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Indent = true
        }))
        {
            doc.Save(xw);
        }
        return sw.ToString();
    }
}
