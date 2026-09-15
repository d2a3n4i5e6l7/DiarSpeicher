using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.Catalog;

/// <summary>Una de las carpetas declaradas en el compose donde pueden vivir bibliotecas.</summary>
public sealed class FolderRootDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Declarada en configuracion pero ausente en disco. Se devuelve igual, con esto en
    /// false, para poder decir "ese disco no esta montado" en vez de callarlo.
    /// </summary>
    [JsonPropertyName("mounted")]
    public bool Mounted { get; set; }
}

/// <summary>Una subcarpeta dentro del listado.</summary>
public sealed class FolderEntryDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Si tiene subcarpetas, para saber si merece la pena entrar.</summary>
    [JsonPropertyName("hasChildren")]
    public bool HasChildren { get; set; }

    /// <summary>
    /// Ficheros sueltos dentro. Es la pista de que una carpeta ya es una biblioteca de
    /// tomos y no un contenedor mas.
    /// </summary>
    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("readable")]
    public bool Readable { get; set; } = true;
}

/// <summary>Contenido de una carpeta.</summary>
public sealed class FolderListingDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Null cuando subir saldria de las carpetas permitidas.</summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("isRoot")]
    public bool IsRoot { get; set; }

    [JsonPropertyName("entries")]
    public List<FolderEntryDto> Entries { get; set; } = [];

    /// <summary>Habia mas entradas de las que cabe devolver.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

/// <summary>Espacio del sistema de ficheros que respalda una carpeta.</summary>
public sealed class DiskUsageDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("freeBytes")]
    public long FreeBytes { get; set; }

    /// <summary>El montaje no reporta cifras: ocurre en algunos sistemas de ficheros de red.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }
}

public sealed class CreateFolderInput
{
    [JsonPropertyName("parent")]
    public string Parent { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Una serie tal y como la crearia el escaner con la estructura elegida.</summary>
public sealed class PreviewSeriesDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("volumeCount")]
    public int VolumeCount { get; set; }

    /// <summary>La carpeta es la raiz de la biblioteca, no una subcarpeta.</summary>
    [JsonPropertyName("isRoot")]
    public bool IsRoot { get; set; }
}

public sealed class ScanPreviewDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("series")]
    public List<PreviewSeriesDto> Series { get; set; } = [];

    [JsonPropertyName("totalVolumes")]
    public int TotalVolumes { get; set; }
}

/// <summary>Lo que se va a destruir, contado antes de tocarlo.</summary>
public sealed class DeletionPreviewDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("folders")]
    public List<DeletionPreviewDto> Folders { get; set; } = [];
}

public sealed class TrashEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("isDirectory")]
    public bool IsDirectory { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTimeOffset DeletedAt { get; set; }

    /// <summary>Segundos que le quedan antes de que se vacie de verdad.</summary>
    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }
}
