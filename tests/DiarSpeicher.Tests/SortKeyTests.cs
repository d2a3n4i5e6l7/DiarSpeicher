using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Tests;

public sealed class SortKeyTests
{
    private static string[] SortedByKey(params string[] input) =>
        [.. input.OrderBy(SortKey.From, StringComparer.Ordinal)];

    [Fact]
    public void VolumesSortByNumberNotByDigit()
    {
        Assert.Equal(
            [
                "Full Metal Panic! Volume 1",
                "Full Metal Panic! Volume 2",
                "Full Metal Panic! Volume 10",
                "Full Metal Panic! Volume 11",
                "Full Metal Panic! Volume 12"
            ],
            SortedByKey(
                "Full Metal Panic! Volume 1",
                "Full Metal Panic! Volume 10",
                "Full Metal Panic! Volume 11",
                "Full Metal Panic! Volume 12",
                "Full Metal Panic! Volume 2"));
    }

    [Fact]
    public void TheSameWordingSortsByItsNumber()
    {
        Assert.Equal(
            ["Vol 1", "Vol 2", "Vol 10"],
            SortedByKey("Vol 10", "Vol 1", "Vol 2"));
    }

    [Fact]
    public void PaddingInTheNameDoesNotChangeTheOrder()
    {
        Assert.Equal(
            ["Tomo 001", "Tomo 002", "Tomo 010", "Tomo 100"],
            SortedByKey("Tomo 100", "Tomo 010", "Tomo 001", "Tomo 002"));
    }

    [Fact]
    public void SeveralNumericSegmentsAreEachPadded()
    {
        Assert.Equal(
            ["v1-99", "v2-01", "v10-01"],
            SortedByKey("v10-01", "v2-01", "v1-99"));
    }

    [Fact]
    public void CaseIsIgnored()
    {
        Assert.Equal(SortKey.From("TOMO 3"), SortKey.From("tomo 3"));
    }

    [Fact]
    public void ANameWithoutDigitsSortsAlphabetically()
    {
        Assert.Equal(
            ["anexo", "prologo", "zeta"],
            SortedByKey("zeta", "anexo", "prologo"));
    }

    [Fact]
    public void AnOversizedRunIsLeftAlone()
    {
        Assert.Equal("isbn 97840123456789", SortKey.From("ISBN 97840123456789"));
    }

    [Fact]
    public void TheKeyAgreesWithTheComparerUsedForPages()
    {
        string[] names = ["Vol 10", "Vol 1", "Vol 2", "Vol 20", "Vol 3"];

        Assert.Equal(
            [.. names.OrderBy(n => n, NaturalSortComparer.OrdinalIgnoreCase)],
            [.. names.OrderBy(SortKey.From, StringComparer.Ordinal)]);
    }

    [Fact]
    public void AnEmptyNameYieldsAnEmptyKey()
    {
        Assert.Equal(string.Empty, SortKey.From(null));
        Assert.Equal(string.Empty, SortKey.From(string.Empty));
    }
}
