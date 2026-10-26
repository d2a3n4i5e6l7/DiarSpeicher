using System.Text.Json.Serialization.Metadata;

namespace DiarSpeicher.Api.Serialization;

internal static class ApiJsonResolvers
{
    public static IJsonTypeInfoResolver Combined { get; } = JsonTypeInfoResolver.Combine(
        ApiJsonContext.Default,
        FilesystemJsonContext.Default,
        CatalogJsonContext.Default,
        KomgaJsonContext.Default,
        ProtocolJsonContext.Default);
}
