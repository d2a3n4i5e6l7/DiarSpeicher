using System.IO.Compression;
using System.Text;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Tests;

/// <summary>
/// ComicInfo.xml can name the cover with Type="FrontCover"; when it does, that page wins over
/// whichever image happens to sort first.
/// </summary>
public sealed class FrontCoverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("diar-cover-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ParserReadsTheDeclaredFrontCover()
    {
        var (metadata, _) = ComicInfoParser.Parse("""
            <?xml version="1.0"?>
            <ComicInfo>
              <Title>Test</Title>
              <Pages>
                <Page Image="0" Type="Story" />
                <Page Image="2" Type="FrontCover" />
                <Page Image="3" Type="Story" />
              </Pages>
            </ComicInfo>
            """);

        Assert.Equal(2, metadata.FrontCoverIndex);
    }

    [Fact]
    public void ParserReturnsNullWhenNoCoverIsDeclared()
    {
        var (withoutPages, _) = ComicInfoParser.Parse("<ComicInfo><Title>Test</Title></ComicInfo>");
        Assert.Null(withoutPages.FrontCoverIndex);

        var (withoutType, _) = ComicInfoParser.Parse("""
            <ComicInfo><Pages><Page Image="0" /></Pages></ComicInfo>
            """);
        Assert.Null(withoutType.FrontCoverIndex);
    }

    [Fact]
    public async Task TheDeclaredCoverIsUsedInsteadOfTheFirstPage()
    {
        var path = Path.Combine(_dir, "declared.cbz");
        WriteCbz(path, frontCoverIndex: 2);

        var result = await new ZipBookProcessor().AnalyzeBookAsync(path, includeCover: true);

        Assert.Equal(4, result.Pages);
        Assert.Equal(2, result.Metadata?.FrontCoverIndex);
        Assert.Equal("PAGE2", Marker(result.Cover!.Data));
    }

    [Fact]
    public async Task WithoutADeclaredCoverTheFirstSortedPageIsUsed()
    {
        var path = Path.Combine(_dir, "undeclared.cbz");
        WriteCbz(path, frontCoverIndex: null);

        var result = await new ZipBookProcessor().AnalyzeBookAsync(path, includeCover: true);

        Assert.Null(result.Metadata?.FrontCoverIndex);
        Assert.Equal("PAGE0", Marker(result.Cover!.Data));
    }

    [Fact]
    public async Task AnOutOfRangeDeclaredCoverFallsBackToTheFirstPage()
    {
        var path = Path.Combine(_dir, "outofrange.cbz");
        WriteCbz(path, frontCoverIndex: 99);

        var result = await new ZipBookProcessor().AnalyzeBookAsync(path, includeCover: true);

        Assert.Equal("PAGE0", Marker(result.Cover!.Data));
    }

    /// <summary>Each page carries its index in the trailing bytes so the cover can be identified.</summary>
    private static byte[] PageMarker(int index) => Encoding.ASCII.GetBytes($"PAGE{index}");

    private static string Marker(byte[] data) => Encoding.ASCII.GetString(data[^5..]);

    private static void WriteCbz(string path, int? frontCoverIndex)
    {
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        for (var i = 0; i < 4; i++)
        {
            var entry = archive.CreateEntry($"{i:D3}.png");
            using var stream = entry.Open();
            stream.Write(PngBuilder.Solid(4, 4));
            stream.Write(PageMarker(i));
        }

        var pages = frontCoverIndex is null
            ? """<Page Image="0" /><Page Image="1" />"""
            : $"""<Page Image="0" /><Page Image="{frontCoverIndex}" Type="FrontCover" />""";

        var info = archive.CreateEntry("ComicInfo.xml");
        using var writer = new StreamWriter(info.Open());
        writer.Write($"<?xml version=\"1.0\"?><ComicInfo><Title>T</Title><Pages>{pages}</Pages></ComicInfo>");
    }
}
