using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.Sync;

public sealed class KoReaderAuthResponse
{
    [JsonPropertyName("authorized")]
    public string Authorized { get; set; } = "OK";
}

public sealed class KoReaderProgressResponse
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Progress { get; set; }

    [JsonPropertyName("percentage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Percentage { get; set; }

    [JsonPropertyName("device")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Device { get; set; }

    [JsonPropertyName("device_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceId { get; set; }

    [JsonPropertyName("timestamp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Timestamp { get; set; }
}

public sealed class KoReaderProgressInput
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public string Progress { get; set; } = string.Empty;

    [JsonPropertyName("percentage")]
    public float Percentage { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }
}

public sealed class KoReaderPutProgressResponse
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public ulong Timestamp { get; set; }
}
