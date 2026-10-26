namespace DiarSpeicher.Infrastructure.Data.Extensions;

/// <summary>
/// The visibility rules are compile-time constants, not SQL assembled per request: whether a
/// rule applies is decided by the parameters (a null one disables its condition), so no query
/// ever formats text into its SQL. The aliases are fixed: l for Libraries, s and sm for Series
/// and its metadata, m and mm for Media and its metadata.
/// </summary>
public static class CatalogSqlFilters
{
    public const string LibraryVisible = """
        (@ExcludedLibraries IS NULL OR l."Id" NOT IN (SELECT value FROM json_each(@ExcludedLibraries)))
        """;

    public const string SeriesVisible = """
        s."DeletedAt" IS NULL
        AND (@ExcludedLibraries IS NULL OR s."LibraryId" IS NULL OR s."LibraryId" NOT IN (SELECT value FROM json_each(@ExcludedLibraries)))
        AND (@MaxAge IS NULL
             OR sm."AgeRating" <= @MaxAge
             OR (@RestrictOnUnset = 0 AND (sm."SeriesId" IS NULL OR sm."AgeRating" IS NULL)))
        """;

    public const string MediaVisible = """
        m."DeletedAt" IS NULL
        AND (@ExcludedLibraries IS NULL OR s."Id" IS NULL OR s."LibraryId" IS NULL OR s."LibraryId" NOT IN (SELECT value FROM json_each(@ExcludedLibraries)))
        AND (@MaxAge IS NULL
             OR mm."AgeRating" <= @MaxAge
             OR (mm."AgeRating" IS NULL
                 AND (sm."AgeRating" <= @MaxAge
                      OR (@RestrictOnUnset = 0 AND (sm."SeriesId" IS NULL OR sm."AgeRating" IS NULL)))))
        """;

    public const string InLibrary = """(@LibraryId IS NULL OR s."LibraryId" = @LibraryId)""";

    public const string InSeries = """(@SeriesId IS NULL OR m."SeriesId" = @SeriesId)""";

    public const string LibrarySearch = """(@SearchPattern IS NULL OR l."Name" LIKE @SearchPattern)""";

    public const string SeriesSearch = """(@SearchPattern IS NULL OR s."Name" LIKE @SearchPattern OR sm."Title" LIKE @SearchPattern)""";

    public const string MediaSearch = """(@SearchPattern IS NULL OR m."Name" LIKE @SearchPattern OR mm."Title" LIKE @SearchPattern)""";

    public static CatalogQueryArgs ArgsFor(AuthUser? user)
    {
        var args = new CatalogQueryArgs();
        if (user == null || user.IsServerOwner) return args;

        if (user.ExcludedLibraryIds.Count > 0)
        {
            args.ExcludedLibraries = SqlKeys.ToJson(user.ExcludedLibraryIds);
        }

        if (user.AgeRestriction.HasValue)
        {
            args.MaxAge = user.AgeRestriction.Value;
            args.RestrictOnUnset = user.RestrictOnUnset ? 1 : 0;
        }

        return args;
    }

    /// <summary>
    /// Fails loudly for any other alias: <see cref="MediaVisible"/> is a constant, so it cannot
    /// follow them, and silently returning SQL that names tables the caller did not alias would
    /// show up as a confusing "no such column".
    /// </summary>
    public static (string WhereClause, CatalogQueryArgs Parameters) BuildMediaVisibilityFilter(
        AuthUser? user,
        string mediaAlias,
        string mediaMetaAlias,
        string seriesAlias,
        string seriesMetaAlias)
    {
        if (mediaAlias != "m" || mediaMetaAlias != "mm" || seriesAlias != "s" || seriesMetaAlias != "sm")
        {
            throw new ArgumentException("The media visibility filter is written for the aliases m, mm, s and sm.");
        }

        return (MediaVisible, ArgsFor(user));
    }
}
