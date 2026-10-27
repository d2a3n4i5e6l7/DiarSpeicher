using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiarSpeicher.Client.Api;

public sealed class NumericBoolConverter : JsonConverter<bool>
{
    public override bool HandleNull => true;

    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False or JsonTokenType.Null => false,
        JsonTokenType.Number => reader.GetInt64() != 0,
        _ => throw new JsonException($"Expected a boolean or a number, got {reader.TokenType}."),
    };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
