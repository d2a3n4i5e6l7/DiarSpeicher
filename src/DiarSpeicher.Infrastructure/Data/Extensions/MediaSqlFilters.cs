namespace DiarSpeicher.Infrastructure.Data.Extensions;

public static class MediaSqlFilters
{
    /// <summary>
    /// Aliases the caller must use: <c>m</c> for Media, <c>mm</c> for MediaMetadata,
    /// <c>s</c> for Series and <c>sm</c> for the series metadata.
    /// </summary>
    public const string Joins = """
        FROM "Media" AS m
        LEFT JOIN "MediaMetadata" AS mm ON m."Id" = mm."MediaId"
        LEFT JOIN "Series" AS s ON m."SeriesId" = s."Id"
        LEFT JOIN "SeriesMetadata" AS sm ON s."Id" = sm."SeriesId"
        """;

    public static (string Where, List<(string Name, object Value)> Parameters) BuildVisibilityFilter(AuthUser? user)
    {
        var clauses = new List<string> { """m."DeletedAt" IS NULL""" };
        var parameters = new List<(string Name, object Value)>();

        if (user is null || user.IsServerOwner)
        {
            return (string.Join(" AND ", clauses), parameters);
        }

        if (user.ExcludedLibraryIds.Count > 0)
        {
            var placeholders = user.ExcludedLibraryIds
                .Select((libraryId, index) =>
                {
                    parameters.Add(($"@excluded{index}", libraryId));
                    return $"@excluded{index}";
                })
                .ToList();

            clauses.Add($"""(s."Id" IS NULL OR s."LibraryId" IS NULL OR s."LibraryId" NOT IN ({string.Join(", ", placeholders)}))""");
        }

        if (user.AgeRestriction.HasValue)
        {
            parameters.Add(("@maxAge", user.AgeRestriction.Value));

            clauses.Add(user.RestrictOnUnset
                ? """
                  (
                      (mm."AgeRating" IS NOT NULL AND mm."AgeRating" <= @maxAge)
                      OR (mm."AgeRating" IS NULL AND sm."AgeRating" IS NOT NULL AND sm."AgeRating" <= @maxAge)
                  )
                  """
                : """
                  (
                      (mm."AgeRating" IS NOT NULL AND mm."AgeRating" <= @maxAge)
                      OR (mm."AgeRating" IS NULL AND (sm."SeriesId" IS NULL OR sm."AgeRating" IS NULL OR sm."AgeRating" <= @maxAge))
                  )
                  """);
        }

        return (string.Join(" AND ", clauses), parameters);
    }

    public static string BuildCountSql(string where) => string.Concat(
        """
        SELECT COUNT(*) AS "Value"

        """,
        Joins,
        """

        WHERE 
        """,
        where);

    public static string BuildLatestPageSql(string where) => string.Concat(
        """
        SELECT m.*

        """,
        Joins,
        """

        WHERE 
        """,
        where,
        """

        ORDER BY m."CreatedAt" DESC, m."Id" DESC
        LIMIT @take OFFSET @skip
        """);

    public static object[] ToParameters(
        this List<(string Name, object Value)> values,
        params (string Name, object Value)[] extra) =>
        [.. values.Concat(extra).Select(p => new SqliteParameter(p.Name, p.Value))];
}
