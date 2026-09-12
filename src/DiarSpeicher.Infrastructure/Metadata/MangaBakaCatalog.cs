using System.Data.Common;
using DiarSpeicher.Core.Domain.MangaBaka;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Metadata;

public interface IMangaBakaCatalog
{
    bool IsAvailable { get; }
    Task<long> CountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MangaBakaCandidateDto>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
    Task<MangaBakaSeriesDto?> GetAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consulta el volcado de MangaBaka. Solo lectura: el fichero lo produce la ingesta y aquí
/// nunca se escribe.
/// <para>
/// El volcado original no trae ni un índice, así que buscar por título sería un escaneo
/// secuencial de 3,5 GB. La ingesta construye una tabla FTS5 (<c>series_fts</c>) y esta
/// clase busca contra ella; si no existiera, la búsqueda cae a LIKE y se nota.
/// </para>
/// </summary>
public class MangaBakaCatalog : IMangaBakaCatalog
{
    private readonly MangaBakaOptions _options;
    private readonly ILogger<MangaBakaCatalog> _logger;

    public MangaBakaCatalog(IOptions<MangaBakaOptions> options, ILogger<MangaBakaCatalog> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable => File.Exists(_options.ResolveSqlitePath());

    private SqliteConnection OpenReadOnly()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.ResolveSqlitePath(),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return 0;

        try
        {
            await using var connection = OpenReadOnly();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM series";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is long count ? count : 0;
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "Could not count the MangaBaka dump");
            return 0;
        }
    }

    public async Task<IReadOnlyList<MangaBakaCandidateDto>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (!IsAvailable || trimmed.Length == 0) return [];

        limit = Math.Clamp(limit, 1, 50);

        await using var connection = OpenReadOnly();
        var useFts = await HasSearchIndexAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        if (useFts)
        {
            command.CommandText = $"""
                SELECT s.id, s.title, s.native_title, s.romanized_title, s.type, s.year,
                       s.status, s.cover_x250_x1, s.rating,
                       s.description, s.authors, s.genres
                FROM series_fts f
                JOIN series s ON s.id = f.rowid
                WHERE series_fts MATCH $needle
                ORDER BY bm25(series_fts), s.popularity_global_current
                LIMIT {limit}
                """;
            command.Parameters.AddWithValue("$needle", ToMatchExpression(trimmed));
        }
        else
        {
            // Sin índice esto es lento a propósito: el camino bueno es el FTS5 y conviene
            // que se note que la ingesta no lo construyó.
            _logger.LogWarning("MangaBaka dump has no series_fts index; falling back to a scan");
            command.CommandText = $"""
                SELECT id, title, native_title, romanized_title, type, year,
                       status, cover_x250_x1, rating,
                       description, authors, genres
                FROM series
                WHERE title LIKE $like OR romanized_title LIKE $like
                LIMIT {limit}
                """;
            command.Parameters.AddWithValue("$like", $"%{trimmed}%");
        }

        var results = new List<MangaBakaCandidateDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCandidate(reader));
        }

        return results;
    }

    public async Task<MangaBakaSeriesDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return null;

        await using var connection = OpenReadOnly();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, native_title, romanized_title, type, year, status,
                   cover_x250_x1, rating,
                   description, authors, genres, artists, publishers,
                   total_chapters, final_volume, canonical_url, links
            FROM series
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var candidate = ReadCandidate(reader);
        return new MangaBakaSeriesDto
        {
            Id = candidate.Id,
            Title = candidate.Title,
            NativeTitle = candidate.NativeTitle,
            RomanizedTitle = candidate.RomanizedTitle,
            Type = candidate.Type,
            Year = candidate.Year,
            Status = candidate.Status,
            CoverUrl = candidate.CoverUrl,
            Rating = candidate.Rating,
            Description = candidate.Description,
            Authors = candidate.Authors,
            Genres = candidate.Genres,
            Artists = ReadString(reader, 12),
            Publishers = ReadString(reader, 13),
            TotalChapters = ReadString(reader, 14),
            FinalVolume = ReadString(reader, 15),
            CanonicalUrl = ReadString(reader, 16),
            Links = ReadString(reader, 17)
        };
    }

    private static MangaBakaCandidateDto ReadCandidate(DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Title = ReadString(reader, 1) ?? string.Empty,
        NativeTitle = ReadString(reader, 2),
        RomanizedTitle = ReadString(reader, 3),
        Type = ReadString(reader, 4),
        Year = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        Status = ReadString(reader, 6),
        CoverUrl = ReadString(reader, 7),
        Rating = reader.IsDBNull(8) ? null : reader.GetDouble(8),
        Description = Shorten(ReadString(reader, 9)),
        Authors = ReadString(reader, 10),
        Genres = ReadString(reader, 11)
    };

    /// <summary>
    /// La sinopsis del volcado puede ocupar paginas. En una lista de candidatos solo hace
    /// falta lo justo para reconocer la obra, y mandarla entera por cada uno de los doce
    /// resultados es ancho de banda tirado.
    /// </summary>
    private static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var clean = text.Trim();
        return clean.Length <= 320 ? clean : clean[..320].TrimEnd() + "...";
    }

    private static string? ReadString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static async Task<bool> HasSearchIndexAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'series_fts'";
        var found = await command.ExecuteScalarAsync(cancellationToken);
        return found is not null;
    }

    /// <summary>
    /// Convierte lo que escribe una persona en una expresión MATCH. Cada palabra se cita
    /// para que los signos del nombre de una carpeta —comillas, guiones, corchetes— no se
    /// lean como sintaxis de FTS5 y revienten la consulta.
    /// </summary>
    private static string ToMatchExpression(string query)
    {
        var words = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Any(char.IsLetterOrDigit))
            .Select(word => "\"" + word.Replace("\"", "\"\"") + "\"");

        return string.Join(" ", words);
    }
}
