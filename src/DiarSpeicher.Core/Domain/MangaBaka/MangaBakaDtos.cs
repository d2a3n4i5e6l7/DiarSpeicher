using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.MangaBaka;

/// <summary>Estado de la ingesta del volcado, tal como lo ve la interfaz.</summary>
public enum MangaBakaState
{
    /// <summary>No hay volcado en disco.</summary>
    Absent,
    Downloading,
    Decompressing,
    Indexing,
    Ready,
    Failed
}

public sealed class MangaBakaStatusDto
{
    [JsonPropertyName("state")]
    public string State { get; set; } = nameof(MangaBakaState.Absent);

    /// <summary>0-100 dentro de la fase actual. Nulo cuando el tamaño no se conoce.</summary>
    [JsonPropertyName("percent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Percent { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("seriesCount")]
    public long SeriesCount { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("busy")]
    public bool Busy { get; set; }
}

/// <summary>
/// Un candidato de emparejado. Lleva lo justo para que una persona decida entre varios
/// homónimos: portada, año, tipo y estado.
/// </summary>
public class MangaBakaCandidateDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("nativeTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeTitle { get; set; }

    [JsonPropertyName("romanizedTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RomanizedTitle { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("year")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Year { get; set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    [JsonPropertyName("coverUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("rating")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Rating { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("authors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Authors { get; set; }

    [JsonPropertyName("genres")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Genres { get; set; }
}

/// <summary>La ficha completa de una serie del volcado.</summary>
public sealed class MangaBakaSeriesDto : MangaBakaCandidateDto
{
    [JsonPropertyName("artists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Artists { get; set; }

    [JsonPropertyName("publishers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publishers { get; set; }

    [JsonPropertyName("totalChapters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TotalChapters { get; set; }

    /// <summary>Último volumen publicado. Es lo que permite decir "3 de 5".</summary>
    [JsonPropertyName("finalVolume")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinalVolume { get; set; }

    [JsonPropertyName("canonicalUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CanonicalUrl { get; set; }

    [JsonPropertyName("links")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Links { get; set; }
}
