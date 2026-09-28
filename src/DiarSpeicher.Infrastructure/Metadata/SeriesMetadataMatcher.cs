using System.Text.RegularExpressions;
using Dapper;
using DiarSpeicher.Core.Domain.MangaBaka;
using DiarSpeicher.Infrastructure.Data;

namespace DiarSpeicher.Infrastructure.Metadata;

public interface ISeriesMetadataMatcher
{
    Task<IReadOnlyList<MangaBakaCandidateDto>> SuggestAsync(string seriesId, int limit, CancellationToken cancellationToken = default);
    Task<bool> ApplyAsync(string seriesId, int mangaBakaId, CancellationToken cancellationToken = default);
    Task<bool> ClearAsync(string seriesId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Empareja una serie local con una del volcado y vuelca sus datos en
/// <see cref="Core.Domain.Entities.SeriesMetadata"/>.
/// </summary>
public class SeriesMetadataMatcher : ISeriesMetadataMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex BracketedRegex =
        new(@"[\[\(\{][^\]\)\}]*[\]\)\}]", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex VolumeWordsRegex =
        new(@"\b(tomos?|vol(umen|ume)?s?|caps?|cap[ií]tulos?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex SeparatorsRegex =
        new(@"[_\.]+", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ExtraSpacesRegex =
        new(@"\s{2,}", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Marca de procedencia en <c>SeriesMetadata.MetaType</c>.</summary>
    public const string SourceTag = "mangabaka";

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IMangaBakaCatalog _catalog;

    public SeriesMetadataMatcher(ISqliteConnectionFactory connectionFactory, IMangaBakaCatalog catalog)
    {
        _connectionFactory = connectionFactory;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<MangaBakaCandidateDto>> SuggestAsync(
        string seriesId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = """SELECT "Name" FROM "Series" WHERE "Id" = @SeriesId LIMIT 1;""";
        var name = await conn.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken));

        if (string.IsNullOrWhiteSpace(name)) return [];

        return await _catalog.SearchAsync(CleanFolderName(name), limit, cancellationToken);
    }

    public async Task<bool> ApplyAsync(string seriesId, int mangaBakaId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string checkSql = """SELECT 1 FROM "Series" WHERE "Id" = @SeriesId LIMIT 1;""";
        var exists = await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition(checkSql, new { SeriesId = seriesId }, cancellationToken: cancellationToken));

        if (!exists.HasValue) return false;

        var external = await _catalog.GetAsync(mangaBakaId, cancellationToken);
        if (external is null) return false;

        var parameters = new
        {
            SeriesId = seriesId,
            MetaType = SourceTag,
            Comicid = external.Id,
            Title = external.Title,
            Summary = external.Description,
            Publisher = external.Publishers,
            Writers = external.Authors,
            Genres = external.Genres,
            Status = external.Status,
            Year = external.Year,
            ComicImage = external.CoverUrl,
            Links = external.CanonicalUrl,
            Booktype = external.Type,
            TotalIssues = ParseCount(external.FinalVolume),
            PublicationRun = external.TotalChapters,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        const string upsertSql = """
            INSERT INTO "SeriesMetadata" (
                "SeriesId", "MetaType", "Comicid", "Title", "Summary", "Publisher", "Writers",
                "Genres", "Status", "Year", "ComicImage", "Links", "Booktype", "TotalIssues", "PublicationRun"
            ) VALUES (
                @SeriesId, @MetaType, @Comicid, @Title, @Summary, @Publisher, @Writers,
                @Genres, @Status, @Year, @ComicImage, @Links, @Booktype, @TotalIssues, @PublicationRun
            )
            ON CONFLICT ("SeriesId") DO UPDATE SET
                "MetaType" = excluded."MetaType",
                "Comicid" = excluded."Comicid",
                "Title" = excluded."Title",
                "Summary" = excluded."Summary",
                "Publisher" = excluded."Publisher",
                "Writers" = excluded."Writers",
                "Genres" = excluded."Genres",
                "Status" = excluded."Status",
                "Year" = excluded."Year",
                "ComicImage" = excluded."ComicImage",
                "Links" = excluded."Links",
                "Booktype" = excluded."Booktype",
                "TotalIssues" = excluded."TotalIssues",
                "PublicationRun" = excluded."PublicationRun";

            UPDATE "Series" SET "UpdatedAt" = @UpdatedAt WHERE "Id" = @SeriesId;
            """;

        await conn.ExecuteAsync(new CommandDefinition(upsertSql, parameters, cancellationToken: cancellationToken));
        return true;
    }

    public async Task<bool> ClearAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = """DELETE FROM "SeriesMetadata" WHERE "SeriesId" = @SeriesId;""";
        var rows = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken));

        return rows > 0;
    }

    internal static string CleanFolderName(string name)
    {
        var cleaned = BracketedRegex.Replace(name, " ");
        cleaned = VolumeWordsRegex.Replace(cleaned, " ");
        cleaned = SeparatorsRegex.Replace(cleaned, " ");
        cleaned = ExtraSpacesRegex.Replace(cleaned, " ");
        return cleaned.Trim();
    }

    private static int? ParseCount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }
}
