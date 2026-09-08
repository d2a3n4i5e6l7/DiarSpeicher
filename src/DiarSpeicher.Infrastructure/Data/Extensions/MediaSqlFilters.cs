using DiarSpeicher.Core.Domain.Models;
using Microsoft.Data.Sqlite;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

/// <summary>
/// The visibility rules of <c>ForUser</c> expressed as raw SQL, for the queries that order by
/// a timestamp. SQLite stores DateTimeOffset as ISO-8601 text, which EF refuses to translate
/// into an ORDER BY, so those queries are written by hand instead of forcing the whole model
/// into a different storage representation.
///
/// The clauses must stay equivalent to <see cref="MediaQueryExtensions.ForUser"/>: this is the
/// filter that keeps an excluded library or an age-restricted book out of a user's results.
/// Values are always bound as parameters, never interpolated into the SQL text.
/// </summary>
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

    /// <summary>
    /// Returns the values as (name, value) pairs rather than SqliteParameter instances: a
    /// parameter object cannot be attached to two commands, and the count and the page are
    /// two separate queries.
    /// </summary>
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

            // A book with its own age rating is judged by it; otherwise the series rating
            // applies. RestrictOnUnset decides what happens when neither carries one.
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

    /// <summary>Fresh SqliteParameter instances, so each command owns its own.</summary>
    public static object[] ToParameters(
        this List<(string Name, object Value)> values,
        params (string Name, object Value)[] extra) =>
        [.. values.Concat(extra).Select(p => new SqliteParameter(p.Name, p.Value))];
}
