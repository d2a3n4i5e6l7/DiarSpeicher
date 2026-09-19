namespace DiarSpeicher.Infrastructure.Data.Extensions;

public static class MediaQueryExtensions
{
    public static IQueryable<Media> MatchingText(this IQueryable<Media> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;

        return query.Where(m =>
            m.Name.Contains(search)
            || m.Metadata!.Title!.Contains(search)
            || m.Metadata.Summary!.Contains(search)
            || m.Metadata.Writers!.Contains(search));
    }

    public static IQueryable<Media> ForUser(this IQueryable<Media> query, AuthUser? user)
    {
        var q = query.Where(m => m.DeletedAt == null);

        if (user == null || user.IsServerOwner)
        {
            return q;
        }

        if (user.ExcludedLibraryIds.Count > 0)
        {
            q = q.Where(m => m.Series == null || m.Series.LibraryId == null || !user.ExcludedLibraryIds.Contains(m.Series.LibraryId));
        }

        if (user.AgeRestriction.HasValue)
        {
            q = ApplyMediaAgeFilter(q, user.AgeRestriction.Value, user.RestrictOnUnset);
        }

        return q;
    }

    public static IQueryable<Series> ForUser(this IQueryable<Series> query, AuthUser? user)
    {
        var q = query.Where(s => s.DeletedAt == null);

        if (user == null || user.IsServerOwner)
        {
            return q;
        }

        if (user.ExcludedLibraryIds.Count > 0)
        {
            q = q.Where(s => s.LibraryId == null || !user.ExcludedLibraryIds.Contains(s.LibraryId));
        }

        if (user.AgeRestriction.HasValue)
        {
            q = ApplySeriesAgeFilter(q, user.AgeRestriction.Value, user.RestrictOnUnset);
        }

        return q;
    }

    public static IQueryable<Library> ForUser(this IQueryable<Library> query, AuthUser? user)
    {
        if (user == null || user.IsServerOwner)
        {
            return query;
        }

        if (user.ExcludedLibraryIds.Count > 0)
        {
            return query.Where(l => !user.ExcludedLibraryIds.Contains(l.Id));
        }

        return query;
    }

    public static IQueryable<Series> WithDetails(this IQueryable<Series> query) =>
        query.Include(s => s.Metadata).Include(s => s.Media);

    private static IQueryable<Series> ApplySeriesAgeFilter(IQueryable<Series> query, int maxAge, bool restrictOnUnset) =>
        restrictOnUnset
            ? query.Where(s => s.Metadata != null && s.Metadata.AgeRating != null && s.Metadata.AgeRating <= maxAge)
            : query.Where(s => s.Metadata == null || s.Metadata.AgeRating == null || s.Metadata.AgeRating <= maxAge);

    private static IQueryable<Media> ApplyMediaAgeFilter(IQueryable<Media> query, int maxAge, bool restrictOnUnset) =>
        restrictOnUnset
            ? ApplyRestrictOnUnsetFilter(query, maxAge)
            : ApplyPermissiveAgeFilter(query, maxAge);

    private static IQueryable<Media> ApplyRestrictOnUnsetFilter(IQueryable<Media> query, int maxAge) =>
        query.Where(m =>
            m.Metadata != null && m.Metadata.AgeRating != null
                ? m.Metadata.AgeRating <= maxAge
                : m.Series != null && m.Series.Metadata != null
                  && m.Series.Metadata.AgeRating != null && m.Series.Metadata.AgeRating <= maxAge);

    private static IQueryable<Media> ApplyPermissiveAgeFilter(IQueryable<Media> query, int maxAge) =>
        query.Where(m =>
            m.Metadata != null && m.Metadata.AgeRating != null
                ? m.Metadata.AgeRating <= maxAge
                : m.Series == null || m.Series.Metadata == null
                  || m.Series.Metadata.AgeRating == null || m.Series.Metadata.AgeRating <= maxAge);
}
