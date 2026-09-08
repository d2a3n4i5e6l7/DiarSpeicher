using System.Diagnostics;
using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Tests;

public sealed class NaturalSortTests
{
    private static string[] Sorted(params string[] input) =>
        [.. input.OrderBy(s => s, NaturalSortComparer.OrdinalIgnoreCase)];

    [Fact]
    public void NumbersSortByValueNotByDigit()
    {
        Assert.Equal(
            ["1.jpg", "2.jpg", "3.jpg", "10.jpg", "11.jpg"],
            Sorted("11.jpg", "3.jpg", "1.jpg", "10.jpg", "2.jpg"));
    }

    [Fact]
    public void PaddingDoesNotChangeTheOrder()
    {
        Assert.Equal(
            ["001.jpg", "002.jpg", "010.jpg", "100.jpg"],
            Sorted("100.jpg", "010.jpg", "001.jpg", "002.jpg"));
    }

    [Fact]
    public void APrefixIsComparedAsTextAndItsNumberAsANumber()
    {
        Assert.Equal(
            ["page1.jpg", "page2.jpg", "page10.jpg", "page20.jpg"],
            Sorted("page10.jpg", "page20.jpg", "page1.jpg", "page2.jpg"));
    }

    [Fact]
    public void SeveralNumericSegmentsAreEachComparedNumerically()
    {
        Assert.Equal(
            ["v1-99.jpg", "v2-01.jpg", "v10-01.jpg"],
            Sorted("v10-01.jpg", "v2-01.jpg", "v1-99.jpg"));
    }

    /// <summary>
    /// The reason paths are compared per component: on the whole string "cap1/p2" sorts
    /// between the "cap01" pages, interleaving two different chapters.
    /// </summary>
    [Fact]
    public void DirectoriesAreComparedLevelByLevel()
    {
        Assert.Equal(
            ["cap1/p2.jpg", "cap01/p01.jpg", "cap01/p10.jpg", "cap02/p01.jpg"],
            Sorted("cap01/p10.jpg", "cap02/p01.jpg", "cap1/p2.jpg", "cap01/p01.jpg"));
    }

    [Fact]
    public void AShallowerPathComesBeforeItsChildren()
    {
        Assert.Equal(
            ["cap01", "cap01/p01.jpg", "cap02"],
            Sorted("cap02", "cap01/p01.jpg", "cap01"));
    }

    [Fact]
    public void BackslashSeparatorsAreTreatedAsPathBoundaries()
    {
        Assert.Equal(
            ["cap1\\p2.jpg", "cap01\\p10.jpg"],
            Sorted("cap01\\p10.jpg", "cap1\\p2.jpg"));
    }

    [Fact]
    public void NumbersTooLargeForALongStillCompare()
    {
        Assert.Equal(
            ["999999999999999999999.jpg", "1000000000000000000000.jpg"],
            Sorted("1000000000000000000000.jpg", "999999999999999999999.jpg"));
    }

    /// <summary>
    /// Two entries differing only in case must land in the same order every run, otherwise
    /// the page order of a book could change between scans.
    /// </summary>
    [Fact]
    public void CaseOnlyDifferencesAreOrderedDeterministically()
    {
        var first = Sorted("X.jpg", "x.jpg", "a.jpg");
        var second = Sorted("x.jpg", "X.jpg", "a.jpg");

        Assert.Equal(first, second);
        Assert.Equal("a.jpg", first[0]);
    }

    [Fact]
    public void NullsAreOrderedWithoutThrowing()
    {
        var comparer = NaturalSortComparer.OrdinalIgnoreCase;

        Assert.Equal(0, comparer.Compare(null, null));
        Assert.True(comparer.Compare(null, "a") < 0);
        Assert.True(comparer.Compare("a", null) > 0);
    }

    [Fact]
    public void SortingAnOmnibusStaysWellUnderASecond()
    {
        var random = new Random(42);
        var pages = Enumerable.Range(1, 3000)
            .Select(i => $"OMNIBUS_vol{random.Next(1, 20)}/page{i:D4}.jpg")
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        Array.Sort(pages, NaturalSortComparer.OrdinalIgnoreCase);
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"sorting 3000 entries took {stopwatch.ElapsedMilliseconds} ms");
    }
}
