using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

/// <summary>
/// Métodos de extensión para replicar las consultas de seguridad y control de acceso de Stump:
/// - stump/crates/models/src/entity/media.rs (get_age_restriction_filter, apply_library_hidden_filter)
/// - stump/crates/models/src/entity/series.rs (get_age_restriction_filter, find_for_user)
/// - stump/crates/models/src/entity/library.rs (find_for_user)
/// </summary>
public static class MediaQueryExtensions
{
    public static IQueryable<Media> ForUser(this IQueryable<Media> query, AuthUser? user)
    {
        var q = query.Where(m => m.DeletedAt == null);

        if (user == null || user.IsServerOwner)
        {
            return q;
        }

        // apply_library_hidden_filter: oculta libros de bibliotecas excluidas
        if (user.ExcludedLibraryIds.Count > 0)
        {
            q = q.Where(m => m.Series == null || m.Series.LibraryId == null || !user.ExcludedLibraryIds.Contains(m.Series.LibraryId));
        }

        // apply_age_restriction_filter: control parental exacto de Stump
        if (user.AgeRestriction.HasValue)
        {
            q = ApplyMediaAgeFilter(q, user.AgeRestriction.Value, user.RestrictOnUnset);
        }

        return q;
    }

    /// <summary>
    /// Transcripción 1:1 de `media.rs::get_age_restriction_filter` de Stump (SeaORM a EF Core).
    /// </summary>
    private static IQueryable<Media> ApplyMediaAgeFilter(IQueryable<Media> query, int maxAge, bool restrictOnUnset)
    {
        if (restrictOnUnset)
        {
            return query.Where(m =>
                // Caso 1: El media no tiene age rating, se defiere al age rating de la serie
                ((m.Metadata == null || m.Metadata.AgeRating == null) &&
                 m.Series != null &&
                 m.Series.Metadata != null &&
                 m.Series.Metadata.AgeRating != null &&
                 m.Series.Metadata.AgeRating <= maxAge)
                ||
                // Caso 2: El media tiene age rating explícito, debe ser <= maxAge
                (m.Metadata != null &&
                 m.Metadata.AgeRating != null &&
                 m.Metadata.AgeRating <= maxAge)
            );
        }

        return query.Where(m =>
            // Caso 1: Sin metadata de media o sin age rating en media
            ((m.Metadata == null || m.Metadata.AgeRating == null) &&
             (
                 // Subcaso 1a: La serie no tiene metadata -> Permitido
                 (m.Series == null || m.Series.Metadata == null)
                 ||
                 // Subcaso 1b: La serie tiene age rating <= maxAge -> Permitido
                 (m.Series != null && m.Series.Metadata != null && m.Series.Metadata.AgeRating != null && m.Series.Metadata.AgeRating <= maxAge)
                 ||
                 // Subcaso 1c: La serie tiene metadata pero no age rating -> Permitido
                 (m.Series != null && m.Series.Metadata != null && m.Series.Metadata.AgeRating == null)
             ))
            ||
            // Caso 2: El media tiene age rating explícito <= maxAge
            (m.Metadata != null &&
             m.Metadata.AgeRating != null &&
             m.Metadata.AgeRating <= maxAge)
        );
    }

    /// <summary>
    /// Transcripción 1:1 de `series.rs::get_age_restriction_filter` de Stump.
    /// </summary>
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
            var maxAge = user.AgeRestriction.Value;
            if (user.RestrictOnUnset)
            {
                q = q.Where(s => s.Metadata != null && s.Metadata.AgeRating != null && s.Metadata.AgeRating <= maxAge);
            }
            else
            {
                q = q.Where(s => s.Metadata == null || s.Metadata.AgeRating == null || s.Metadata.AgeRating <= maxAge);
            }
        }

        return q;
    }

    /// <summary>
    /// Transcripción 1:1 de `library.rs::find_for_user` de Stump.
    /// </summary>
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
}
