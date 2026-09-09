using DiarSpeicher.Core.Gateway;

namespace DiarSpeicher.Tests;

/// <summary>
/// Link prefix for tests. <see cref="Root"/> is the default in most of them —served from the
/// root, links unchanged— while <see cref="Under"/> covers being mounted behind a gateway
/// prefix.
/// </summary>
internal sealed class TestLinkPrefix : ILinkPrefixProvider
{
    public static ILinkPrefixProvider Root { get; } = new TestLinkPrefix(string.Empty);

    public static ILinkPrefixProvider Under(string prefix) => new TestLinkPrefix("/" + prefix.Trim('/'));

    private TestLinkPrefix(string prefix) => Prefix = prefix;

    public string Prefix { get; }
}
