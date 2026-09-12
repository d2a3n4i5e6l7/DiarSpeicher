using System.Text.Json.Serialization;
using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Core.Domain.StumpV2;

public sealed class StumpPageResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}

public sealed class StumpMediaDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; set; }

    [JsonPropertyName("koreaderHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KoreaderHash { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("seriesId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SeriesId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StumpMediaMetadataDto? Metadata { get; set; }

    [JsonPropertyName("currentPage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }
}

public sealed class StumpMediaMetadataDto
{
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonPropertyName("writers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Writers { get; set; }

    [JsonPropertyName("genre")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Genre { get; set; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    [JsonPropertyName("ageRating")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AgeRating { get; set; }

    [JsonPropertyName("number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Number { get; set; }
}

public sealed class StumpSeriesDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("mediaCount")]
    public int MediaCount { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Marca del ultimo cambio de portada. El cliente la cuelga de la URL de la miniatura
    /// para que el navegador no siga sirviendo la anterior desde su cache.
    /// </summary>
    [JsonPropertyName("coverUpdatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverUpdatedAt { get; set; }

    /// <summary>
    /// Ficha externa, si la serie esta emparejada. Ausente mientras nadie la haya
    /// emparejado, que es como el frontend distingue una serie sin identificar.
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StumpSeriesMetadataDto? Metadata { get; set; }
}

/// <summary>
/// Lo que aporta el catalogo externo sobre una serie. <c>source</c> dice de donde salio,
/// para que la interfaz pueda marcarlo y ofrecer revertirlo.
/// </summary>
public sealed class StumpSeriesMetadataDto
{
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    /// <summary>Id en el catalogo externo, para reabrir la ficha de origen.</summary>
    [JsonPropertyName("externalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExternalId { get; set; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    [JsonPropertyName("writers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Writers { get; set; }

    [JsonPropertyName("genres")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Genres { get; set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    [JsonPropertyName("year")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Year { get; set; }

    [JsonPropertyName("coverUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; set; }

    /// <summary>Ultimo volumen publicado: con el se dice "3 de 12".</summary>
    [JsonPropertyName("finalVolume")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FinalVolume { get; set; }

    /// <summary>Manga, novela, manhwa... tal como lo clasifica el catalogo externo.</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    /// <summary>Capitulos publicados en total.</summary>
    [JsonPropertyName("totalChapters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TotalChapters { get; set; }
}

public sealed class StumpLibraryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("seriesCount")]
    public int SeriesCount { get; set; }

    [JsonPropertyName("mediaCount")]
    public int MediaCount { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("emoji")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Emoji { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("lastScannedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastScannedAt { get; set; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StumpLibraryConfigDto? Config { get; set; }
}

public sealed class StumpLibraryConfigDto
{
    [JsonPropertyName("libraryType")]
    public string LibraryType { get; set; } = nameof(Enums.LibraryType.Mixed);

    [JsonPropertyName("libraryPattern")]
    public string LibraryPattern { get; set; } = nameof(Enums.LibraryPattern.SeriesBased);

    [JsonPropertyName("defaultReadingDir")]
    public string DefaultReadingDir { get; set; } = nameof(ReadingDirection.LeftToRight);

    [JsonPropertyName("defaultReadingMode")]
    public string DefaultReadingMode { get; set; } = nameof(ReadingMode.Paged);

    [JsonPropertyName("defaultLibraryViewMode")]
    public string DefaultLibraryViewMode { get; set; } = nameof(LibraryViewMode.Grid);

    [JsonPropertyName("convertRarToZip")]
    public bool ConvertRarToZip { get; set; }

    [JsonPropertyName("hardDeleteConversions")]
    public bool HardDeleteConversions { get; set; }

    [JsonPropertyName("generateFileHashes")]
    public bool GenerateFileHashes { get; set; } = true;

    [JsonPropertyName("generateKoreaderHashes")]
    public bool GenerateKoreaderHashes { get; set; } = true;

    [JsonPropertyName("processMetadata")]
    public bool ProcessMetadata { get; set; } = true;

    [JsonPropertyName("watch")]
    public bool Watch { get; set; }

    [JsonPropertyName("hideSeriesView")]
    public bool HideSeriesView { get; set; }

    [JsonPropertyName("thumbnailWidth")]
    public int ThumbnailWidth { get; set; } = 400;

    [JsonPropertyName("thumbnailHeight")]
    public int ThumbnailHeight { get; set; } = 600;

    [JsonPropertyName("ignoreRules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IgnoreRules { get; set; }
}

public sealed class StumpEpubTocDto
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<StumpEpubTocItem> Items { get; set; } = new();
}

public sealed class StumpEpubTocItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("children")]
    public List<StumpEpubTocItem> Children { get; set; } = new();
}

public sealed class StumpUpdateProgressInput
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("percentage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Percentage { get; set; }

    [JsonPropertyName("isCompleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsCompleted { get; set; }
}

public sealed class StumpSystemStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "OK";

    [JsonPropertyName("semver")]
    public string Semver { get; set; } = "0.1.0";

    [JsonPropertyName("isClaimed")]
    public bool IsClaimed { get; set; }
}

public sealed class StumpCreateLibraryInput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }

    [JsonPropertyName("config")]
    public StumpLibraryConfigDto? Config { get; set; }
}

/// <summary>
/// Cuerpo de PUT /api/v2/libraries/{id}. Un campo nulo significa "no tocar", de modo que el
/// cliente puede enviar solo lo que cambia sin arrastrar el resto del recurso.
/// </summary>
public sealed class StumpUpdateLibraryInput
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("emoji")]
    public string? Emoji { get; set; }

    [JsonPropertyName("config")]
    public StumpLibraryConfigDto? Config { get; set; }
}

/// <summary>
/// Cuerpo de PUT /api/v2/series/{id}. Un campo nulo significa "no tocar".
/// <para>
/// El nombre de una serie sale del nombre de su carpeta, y ahi acaban cosas como
/// "Accel World Tomos [01-08][Completo]" o, si la serie cuelga de la raiz de la
/// biblioteca, el nombre de la propia biblioteca. El escaner solo lo escribe al crear la
/// serie, nunca despues, asi que un renombrado a mano sobrevive a los rescaneos.
/// </para>
/// </summary>
public sealed class StumpUpdateSeriesInput
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>Cuerpo de PUT /api/v2/series/{id}/thumbnail: el tomo cuya portada se adopta.</summary>
public sealed class StumpSeriesThumbnailInput
{
    [JsonPropertyName("mediaId")]
    public string MediaId { get; set; } = string.Empty;
}

public sealed class StumpUploadResponseDto
{
    [JsonPropertyName("uploadedCount")]
    public int UploadedCount { get; set; }

    [JsonPropertyName("files")]
    public List<UploadedFileDto> Files { get; set; } = new();

    [JsonPropertyName("scanJobTriggered")]
    public bool ScanJobTriggered { get; set; }
}

public sealed class UploadedFileDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public enum UploadOutcome
{
    Success,
    UploadDisabled,
    PermissionDenied,
    LibraryNotFound,
    NoAcceptedFiles,
    FileTooLarge,
    ExtensionNotAllowed
}

public sealed class UploadResult
{
    public UploadOutcome Outcome { get; init; }
    public StumpUploadResponseDto? Response { get; init; }
    public string? Message { get; init; }

    public static UploadResult Ok(StumpUploadResponseDto response) =>
        new() { Outcome = UploadOutcome.Success, Response = response };

    public static UploadResult Fail(UploadOutcome outcome, string message) =>
        new() { Outcome = outcome, Message = message };
}

public sealed class StumpUploadFileInput
{
    public string FileName { get; set; } = string.Empty;
    public Stream Content { get; set; } = Stream.Null;
}


