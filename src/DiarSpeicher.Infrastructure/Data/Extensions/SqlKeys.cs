using System.Text.Json;
using DiarSpeicher.Infrastructure.Serialization;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

/// <summary>
/// Keys travel as a single JSON array read with json_each: expanding IN @list needs vanilla
/// Dapper at runtime and creates one parameter per element, and the Kobo sync passes the whole
/// library.
/// </summary>
public static class SqlKeys
{
    public static string ToJson(IEnumerable<string?> keys) =>
        JsonSerializer.Serialize(keys.OfType<string>().Distinct(StringComparer.Ordinal).ToList(), InfrastructureJsonContext.Default.ListString);

    public static string ToJson(IEnumerable<int> keys) =>
        JsonSerializer.Serialize(keys.Distinct().ToList(), InfrastructureJsonContext.Default.ListInt32);
}
