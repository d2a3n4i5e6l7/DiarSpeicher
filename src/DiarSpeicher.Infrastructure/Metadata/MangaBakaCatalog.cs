using System.Data.Common;
using DiarSpeicher.Core.Domain.MangaBaka;

namespace DiarSpeicher.Infrastructure.Metadata;

public interface IMangaBakaCatalog
{
    bool IsAvailable { get; }
    Task<long> CountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MangaBakaCandidateDto>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
    Task<MangaBakaSeriesDto?> GetAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read only: the ingest produces the file and nothing is written here. The original dump has
/// no index, so searching by title would be a sequential scan of 3.5 GB. The ingest builds an
/// FTS5 table (<c>series_fts</c>) and this class searches it; without it the search falls back
/// to LIKE, noticeably slower.
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

        var path = _options.ResolveSqlitePath();
        var stamp = ReadStamp(path);
        if (_countCache is { } cached && cached.Stamp == stamp) return cached.Total;

        try
        {
            await using var connection = OpenReadOnly();
            await using var command = connection.CreateCommand();
            command.CommandText = await HasSearchIndexAsync(connection, cancellationToken)
                ? "SELECT COUNT(*) FROM series_fts_docsize"
                : "SELECT COALESCE(MAX(rowid), 0) FROM series";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var total = result is long value ? value : 0;

            _countCache = (stamp, total);
            return total;
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "Could not count the MangaBaka dump");
            return 0;
        }
    }

    /// <summary>
    /// The dump does not change until it is ingested again, so the count is cached. The stamp
    /// is file size and date: a new dump invalidates it.
    /// </summary>
    private (string Stamp, long Total)? _countCache;

    private static string ReadStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
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
            command.CommandText = """
                SELECT s.id, s.title, s.native_title, s.romanized_title, s.type, s.year,
                       s.status, s.cover_x250_x1, s.rating,
                       s.description, s.authors, s.genres
                FROM series_fts f
                JOIN series s ON s.id = f.rowid
                WHERE series_fts MATCH $needle
                ORDER BY bm25(series_fts), s.popularity_global_current
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$needle", ToMatchExpression(trimmed));
            command.Parameters.AddWithValue("$limit", limit);
        }
        else
        {
            // Slow without an index on purpose: the good path is FTS5, and it should be
            // noticeable that the ingest did not build it.
            _logger.LogWarning("MangaBaka dump has no series_fts index; falling back to a scan");
            command.CommandText = """
                SELECT id, title, native_title, romanized_title, type, year,
                       status, cover_x250_x1, rating,
                       description, authors, genres
                FROM series
                WHERE title LIKE $like OR romanized_title LIKE $like
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$like", $"%{trimmed}%");
            command.Parameters.AddWithValue("$limit", limit);
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
            Description = ReadString(reader, 9),
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
    /// The dump synopsis can span pages. A candidate list only needs enough to recognize the
    /// work, and sending it whole for each of the twelve results is wasted bandwidth.
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
    /// Every word is quoted so the punctuation of a folder name (quotes, dashes, brackets) is
    /// not read as FTS5 syntax and does not break the query.
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
