using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Api.GraphQL;

/// <summary>
/// Nested relations are resolved through DataLoaders so that querying media across N series
/// costs one batched query instead of N. Every loader re-applies ForUser: a nested field is
/// as reachable as a root field, so the filter cannot be applied only at the top.
/// </summary>
[ExtendObjectType<Series>]
public class SeriesResolvers
{
    protected SeriesResolvers() { }

    public static async Task<IReadOnlyList<Media>> GetMedia(
        [Parent] Series series,
        MediaBySeriesDataLoader mediaLoader,
        CancellationToken ct)
    {
        var media = await mediaLoader.LoadAsync(series.Id, ct);

        return media ?? [];
    }

    public static async Task<Library?> GetLibrary(
        [Parent] Series series,
        LibraryByIdDataLoader libraryLoader,
        CancellationToken ct) =>
        series.LibraryId == null ? null : await libraryLoader.LoadAsync(series.LibraryId, ct);
}

[ExtendObjectType<Media>]
public class MediaResolvers
{
    protected MediaResolvers() { }

    public static async Task<Series?> GetSeries(
        [Parent] Media media,
        SeriesByIdDataLoader seriesLoader,
        CancellationToken ct) =>
        media.SeriesId == null ? null : await seriesLoader.LoadAsync(media.SeriesId, ct);
}

[ExtendObjectType<Library>]
public class LibraryResolvers
{
    protected LibraryResolvers() { }

    public static async Task<IReadOnlyList<Series>> GetSeries(
        [Parent] Library library,
        SeriesByLibraryDataLoader seriesLoader,
        CancellationToken ct)
    {
        var series = await seriesLoader.LoadAsync(library.Id, ct);

        return series ?? [];
    }
}

public sealed class MediaBySeriesDataLoader : GroupedDataLoader<string, Media>
{
    private readonly IDbContextFactory<DiarSpeicherDbContext> _dbFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MediaBySeriesDataLoader(
        IDbContextFactory<DiarSpeicherDbContext> dbFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _dbFactory = dbFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<ILookup<string, Media>> LoadGroupedBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var media = await db.Media
            .ForUser(_httpContextAccessor.GetAuthUser())
            .Where(m => m.SeriesId != null && keys.Contains(m.SeriesId))
            .ToListAsync(cancellationToken);

        return media.ToLookup(m => m.SeriesId!);
    }
}

public sealed class SeriesByLibraryDataLoader : GroupedDataLoader<string, Series>
{
    private readonly IDbContextFactory<DiarSpeicherDbContext> _dbFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SeriesByLibraryDataLoader(
        IDbContextFactory<DiarSpeicherDbContext> dbFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _dbFactory = dbFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<ILookup<string, Series>> LoadGroupedBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var series = await db.Series
            .ForUser(_httpContextAccessor.GetAuthUser())
            .Where(s => s.LibraryId != null && keys.Contains(s.LibraryId))
            .ToListAsync(cancellationToken);

        return series.ToLookup(s => s.LibraryId!);
    }
}

public sealed class SeriesByIdDataLoader : BatchDataLoader<string, Series>
{
    private readonly IDbContextFactory<DiarSpeicherDbContext> _dbFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SeriesByIdDataLoader(
        IDbContextFactory<DiarSpeicherDbContext> dbFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _dbFactory = dbFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<IReadOnlyDictionary<string, Series>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Series
            .ForUser(_httpContextAccessor.GetAuthUser())
            .Where(s => keys.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
    }
}

public sealed class LibraryByIdDataLoader : BatchDataLoader<string, Library>
{
    private readonly IDbContextFactory<DiarSpeicherDbContext> _dbFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LibraryByIdDataLoader(
        IDbContextFactory<DiarSpeicherDbContext> dbFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _dbFactory = dbFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<IReadOnlyDictionary<string, Library>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Libraries
            .ForUser(_httpContextAccessor.GetAuthUser())
            .Where(l => keys.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);
    }
}

internal static class AuthUserAccessor
{
    public static AuthUser? GetAuthUser(this IHttpContextAccessor accessor) =>
        accessor.HttpContext?.Items[GraphQLConstants.AuthUserKey] as AuthUser;
}
