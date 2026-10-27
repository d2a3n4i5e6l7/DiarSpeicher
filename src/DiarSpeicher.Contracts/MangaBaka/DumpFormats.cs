namespace DiarSpeicher.Core.Domain.MangaBaka;

public static class DumpFormats
{
    public static readonly IReadOnlyList<string> Extensions = [".zst", ".tar.gz", ".tgz"];

    public static bool IsAccepted(string name) =>
        Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
