using Dapper;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

public static class CatalogSqlFilters
{
    public static (string WhereClause, DynamicParameters Parameters) BuildLibraryVisibilityFilter(
        AuthUser? user,
        string tableAlias = "l")
    {
        var parameters = new DynamicParameters();
        var clauses = new List<string>();

        if (user != null && !user.IsServerOwner && user.ExcludedLibraryIds.Count > 0)
        {
            clauses.Add($"{tableAlias}.\"Id\" NOT IN @ExcludedLibraries");
            parameters.Add("ExcludedLibraries", user.ExcludedLibraryIds);
        }

        var where = clauses.Count > 0 ? string.Join(" AND ", clauses) : "1=1";
        return (where, parameters);
    }

    public static (string WhereClause, DynamicParameters Parameters) BuildSeriesVisibilityFilter(
        AuthUser? user,
        string seriesAlias = "s",
        string seriesMetaAlias = "sm")
    {
        var parameters = new DynamicParameters();
        var clauses = new List<string> { $"{seriesAlias}.\"DeletedAt\" IS NULL" };

        if (user != null && !user.IsServerOwner)
        {
            if (user.ExcludedLibraryIds.Count > 0)
            {
                clauses.Add($"({seriesAlias}.\"LibraryId\" IS NULL OR {seriesAlias}.\"LibraryId\" NOT IN @ExcludedLibraries)");
                parameters.Add("ExcludedLibraries", user.ExcludedLibraryIds);
            }

            if (user.AgeRestriction.HasValue)
            {
                parameters.Add("MaxAge", user.AgeRestriction.Value);
                if (user.RestrictOnUnset)
                {
                    clauses.Add($"({seriesMetaAlias}.\"AgeRating\" IS NOT NULL AND {seriesMetaAlias}.\"AgeRating\" <= @MaxAge)");
                }
                else
                {
                    clauses.Add($"({seriesMetaAlias}.\"SeriesId\" IS NULL OR {seriesMetaAlias}.\"AgeRating\" IS NULL OR {seriesMetaAlias}.\"AgeRating\" <= @MaxAge)");
                }
            }
        }

        return (string.Join(" AND ", clauses), parameters);
    }

    public static (string WhereClause, DynamicParameters Parameters) BuildMediaVisibilityFilter(
        AuthUser? user,
        string mediaAlias = "m",
        string mediaMetaAlias = "mm",
        string seriesAlias = "s",
        string seriesMetaAlias = "sm")
    {
        var parameters = new DynamicParameters();
        var clauses = new List<string> { $"{mediaAlias}.\"DeletedAt\" IS NULL" };

        if (user != null && !user.IsServerOwner)
        {
            if (user.ExcludedLibraryIds.Count > 0)
            {
                clauses.Add($"({seriesAlias}.\"Id\" IS NULL OR {seriesAlias}.\"LibraryId\" IS NULL OR {seriesAlias}.\"LibraryId\" NOT IN @ExcludedLibraries)");
                parameters.Add("ExcludedLibraries", user.ExcludedLibraryIds);
            }

            if (user.AgeRestriction.HasValue)
            {
                parameters.Add("MaxAge", user.AgeRestriction.Value);
                if (user.RestrictOnUnset)
                {
                    clauses.Add($"""
                        (
                            ({mediaMetaAlias}."AgeRating" IS NOT NULL AND {mediaMetaAlias}."AgeRating" <= @MaxAge)
                            OR ({mediaMetaAlias}."AgeRating" IS NULL AND {seriesMetaAlias}."AgeRating" IS NOT NULL AND {seriesMetaAlias}."AgeRating" <= @MaxAge)
                        )
                        """);
                }
                else
                {
                    clauses.Add($"""
                        (
                            ({mediaMetaAlias}."AgeRating" IS NOT NULL AND {mediaMetaAlias}."AgeRating" <= @MaxAge)
                            OR ({mediaMetaAlias}."AgeRating" IS NULL AND ({seriesMetaAlias}."SeriesId" IS NULL OR {seriesMetaAlias}."AgeRating" IS NULL OR {seriesMetaAlias}."AgeRating" <= @MaxAge))
                        )
                        """);
                }
            }
        }

        return (string.Join(" AND ", clauses), parameters);
    }
}
