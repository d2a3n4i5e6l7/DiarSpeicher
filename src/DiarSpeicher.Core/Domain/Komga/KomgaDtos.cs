using System.Text.Json.Serialization;

namespace DiarSpeicher.Core.Domain.Komga;

public class KomgaPageResponse<T>
{
    [JsonPropertyName("content")]
    public List<T> Content { get; set; } = [];

    [JsonPropertyName("pageable")]
    public KomgaPageableDto Pageable { get; set; } = new();

    [JsonPropertyName("totalElements")]
    public long TotalElements { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("last")]
    public bool Last { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("first")]
    public bool First { get; set; }

    [JsonPropertyName("numberOfElements")]
    public int NumberOfElements { get; set; }

    [JsonPropertyName("empty")]
    public bool Empty { get; set; }

    public static KomgaPageResponse<T> Create(List<T> items, int page, int size, int totalElements)
    {
        var totalPages = size > 0 ? (int)Math.Ceiling(totalElements / (double)size) : 1;
        return new KomgaPageResponse<T>
        {
            Content = items,
            Pageable = new KomgaPageableDto
            {
                PageNumber = page,
                PageSize = size,
                Offset = (long)page * size
            },
            TotalElements = totalElements,
            TotalPages = totalPages,
            Last = page >= totalPages - 1,
            Size = size,
            Number = page,
            First = page == 0,
            NumberOfElements = items.Count,
            Empty = items.Count == 0
        };
    }
}

public class KomgaPageableDto
{
    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 20;

    [JsonPropertyName("offset")]
    public long Offset { get; set; }

    [JsonPropertyName("paged")]
    public bool Paged { get; set; } = true;

    [JsonPropertyName("unpaged")]
    public bool Unpaged { get; set; } = false;
}

public class KomgaLibraryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("root")]
    public string Root { get; set; } = string.Empty;

    [JsonPropertyName("importComicInfoBook")]
    public bool ImportComicInfoBook { get; set; } = true;

    [JsonPropertyName("importComicInfoSeries")]
    public bool ImportComicInfoSeries { get; set; } = true;

    [JsonPropertyName("importEpubBook")]
    public bool ImportEpubBook { get; set; } = true;

    [JsonPropertyName("importEpubSeries")]
    public bool ImportEpubSeries { get; set; } = true;

    [JsonPropertyName("unavailable")]
    public bool Unavailable { get; set; } = false;
}

public class KomgaSeriesDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;

    [JsonPropertyName("lastModified")]
    public string LastModified { get; set; } = string.Empty;

    [JsonPropertyName("fileLastModified")]
    public string FileLastModified { get; set; } = string.Empty;

    [JsonPropertyName("booksCount")]
    public int BooksCount { get; set; }

    [JsonPropertyName("booksReadCount")]
    public int BooksReadCount { get; set; }

    [JsonPropertyName("booksUnreadCount")]
    public int BooksUnreadCount { get; set; }

    [JsonPropertyName("booksInProgressCount")]
    public int BooksInProgressCount { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaSeriesMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; } = false;
}

public class KomgaSeriesMetadataDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ONGOING";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("ageRating")]
    public int? AgeRating { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

public class KomgaBookDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("seriesId")]
    public string SeriesId { get; set; } = string.Empty;

    [JsonPropertyName("seriesTitle")]
    public string SeriesTitle { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public int Number { get; set; } = 1;

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;

    [JsonPropertyName("lastModified")]
    public string LastModified { get; set; } = string.Empty;

    [JsonPropertyName("fileLastModified")]
    public string FileLastModified { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;

    [JsonPropertyName("media")]
    public KomgaMediaDto Media { get; set; } = new();

    [JsonPropertyName("metadata")]
    public KomgaBookMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("readProgress")]
    public KomgaReadProgressDto? ReadProgress { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; } = false;
}

public class KomgaMediaDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "READY";

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "application/vnd.comicbook+zip";

    [JsonPropertyName("pagesCount")]
    public int PagesCount { get; set; }

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;
}

public class KomgaAuthorDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "writer";
}

public class KomgaBookMetadataDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = "1";

    [JsonPropertyName("numberSort")]
    public double NumberSort { get; set; } = 1.0;

    [JsonPropertyName("authors")]
    public List<KomgaAuthorDto> Authors { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("isbn")]
    public string? Isbn { get; set; }
}

public class KomgaReadProgressDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("readDate")]
    public string ReadDate { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;

    [JsonPropertyName("lastModified")]
    public string LastModified { get; set; } = string.Empty;
}

public class KomgaBookPageDto
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = "image/jpeg";

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Komga manda siempre este campo y nunca nulo: cadena vacia cuando no hay
    /// <see cref="SizeBytes"/>. Un cliente que lo espere obligatorio no puede deserializar
    /// la lista de paginas si falta, y sin lista no abre ninguna.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size => SizeBytes.HasValue ? FormatBinary(SizeBytes.Value) : string.Empty;

    /// <summary>Unidades binarias, como el BinaryByteUnit que usa Komga.</summary>
    private static string FormatBinary(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.#} {units[unit]}";
    }
}

public class KomgaReadProgressUpdateDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }
}
