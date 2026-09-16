using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Tests;

internal static class TestLibraryRoots
{
    public static DiarSpeicherServiceOptions Allowing(string root) => new()
    {
        LibraryRoots = Options.Create(new LibraryRootsOptions { Roots = [root] })
    };
}
