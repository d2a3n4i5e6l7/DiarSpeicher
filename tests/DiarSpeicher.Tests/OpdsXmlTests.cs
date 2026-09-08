using System.Xml.Linq;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Infrastructure.Opds;

namespace DiarSpeicher.Tests;

public class OpdsXmlTests
{
    [Fact]
    public void BuildFeedXml_GeneratesValidAtomFeedWithNamespaces()
    {
        var feed = new OpdsFeed
        {
            Id = "test_feed_id",
            Title = "Test OPDS Feed",
            Updated = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
            Author = new OpdsAuthor("DiarSpeicher", "https://github.com/diarmund/DiarSpeicher"),
            Links =
            [
                new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, "/opds/v1.2/catalog"),
                new(OpdsLinkType.Navigation, OpdsLinkRel.Start, "/opds/v1.2/catalog")
            ],
            Entries =
            [
                new()
                {
                    Id = "book_1",
                    Title = "Spider-Man #01",
                    Updated = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
                    Summary = "First issue summary",
                    Content = "25.5 MiB - cbz<br/><br/>First issue summary",
                    Authors = ["Stan Lee", "Steve Ditko"],
                    Links =
                    [
                        new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Thumbnail, "/opds/v1.2/books/book_1/thumbnail"),
                        new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Image, "/opds/v1.2/books/book_1/pages/0?zero_based=true"),
                        new(OpdsLinkType.Zip, OpdsLinkRel.Acquisition, "/opds/v1.2/books/book_1/file/spiderman01.cbz")
                    ],
                    StreamLink = new OpdsStreamLink(
                        "book_1",
                        32,
                        OpdsLinkType.ImageJpeg,
                        5,
                        "2026-09-08T12:00:00+00:00",
                        "/opds/v1.2/books/book_1/pages/{pageNumber}?zero_based=true"
                    )
                }
            ]
        };

        var xml = OpdsXmlBuilder.BuildFeedXml(feed);

        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal("feed", root.Name.LocalName);
        Assert.Equal("http://www.w3.org/2005/Atom", root.Name.NamespaceName);

        // Namespace declarations
        Assert.Equal("http://opds-spec.org/2010/catalog", root.GetNamespaceOfPrefix("opds")?.NamespaceName);
        Assert.Equal("http://vaemendis.net/opds-pse/ns", root.GetNamespaceOfPrefix("pse")?.NamespaceName);

        // Core feed elements
        XNamespace atom = "http://www.w3.org/2005/Atom";
        Assert.Equal("test_feed_id", root.Element(atom + "id")?.Value);
        Assert.Equal("Test OPDS Feed", root.Element(atom + "title")?.Value);

        // Author
        var author = root.Element(atom + "author");
        Assert.NotNull(author);
        Assert.Equal("DiarSpeicher", author.Element(atom + "name")?.Value);

        // Entry
        var entry = root.Element(atom + "entry");
        Assert.NotNull(entry);
        Assert.Equal("Spider-Man #01", entry.Element(atom + "title")?.Value);
        Assert.Equal("book_1", entry.Element(atom + "id")?.Value);

        // Authors in entry
        var entryAuthors = entry.Elements(atom + "author").ToList();
        Assert.Single(entryAuthors);
        var authorNames = entryAuthors[0].Elements(atom + "name").Select(n => n.Value).ToList();
        Assert.Equal(["Stan Lee", "Steve Ditko"], authorNames);

        // PSE Stream Link
        XNamespace pse = "http://vaemendis.net/opds-pse/ns";
        var streamLink = entry.Elements(atom + "link")
            .FirstOrDefault(l => l.Attribute("rel")?.Value == "http://vaemendis.net/opds-pse/stream");
        Assert.NotNull(streamLink);
        Assert.Equal("32", streamLink.Attribute(pse + "count")?.Value);
        Assert.Equal("5", streamLink.Attribute(pse + "lastRead")?.Value);
    }

    [Fact]
    public void BuildOpenSearchXml_GeneratesValidOpenSearchDocument()
    {
        var xml = OpdsXmlBuilder.BuildOpenSearchXml("/opds/v1.2/search/feed?search={searchTerms}");

        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal("OpenSearchDescription", root.Name.LocalName);
        Assert.Equal("http://a9.com/-/spec/opensearch/1.1/", root.Name.NamespaceName);

        XNamespace os = "http://a9.com/-/spec/opensearch/1.1/";
        Assert.Equal("Search", root.Element(os + "ShortName")?.Value);
        Assert.Equal("Search by keyword", root.Element(os + "Description")?.Value);

        var url = root.Element(os + "Url");
        Assert.NotNull(url);
        Assert.Equal("/opds/v1.2/search/feed?search={searchTerms}", url.Attribute("template")?.Value);
        Assert.Equal(OpdsLinkType.Acquisition, url.Attribute("type")?.Value);
    }
}
