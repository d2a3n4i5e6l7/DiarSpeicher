namespace DiarSpeicher.Client.Catalog;

public static class OriginLink
{
    public static string Link(string href, string from, string label) =>
        $"{href}?from={Uri.EscapeDataString(from)}&label={Uri.EscapeDataString(label)}";
}
