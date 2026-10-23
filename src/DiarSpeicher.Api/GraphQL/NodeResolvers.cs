using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using HotChocolate.Execution.Configuration;

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
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MediaBySeriesDataLoader(
        ISqliteConnectionFactory connectionFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<ILookup<string, Media>> LoadGroupedBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return Array.Empty<Media>().ToLookup(m => m.SeriesId!);

        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var user = _httpContextAccessor.GetAuthUser();
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("SeriesKeys", keys);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."SeriesId" IN @SeriesKeys AND {where};
            """;

        var media = await conn.QueryAsync<Media>(sql, parameters);
        return media.ToLookup(m => m.SeriesId!);
    }
}

public sealed class SeriesByLibraryDataLoader : GroupedDataLoader<string, Series>
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SeriesByLibraryDataLoader(
        ISqliteConnectionFactory connectionFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<ILookup<string, Series>> LoadGroupedBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return Array.Empty<Series>().ToLookup(s => s.LibraryId!);

        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var user = _httpContextAccessor.GetAuthUser();
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        parameters.Add("LibraryKeys", keys);

        var sql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" IN @LibraryKeys AND {where};
            """;

        var series = await conn.QueryAsync<Series>(sql, parameters);
        return series.ToLookup(s => s.LibraryId!);
    }
}

public sealed class SeriesByIdDataLoader : BatchDataLoader<string, Series>
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SeriesByIdDataLoader(
        ISqliteConnectionFactory connectionFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<IReadOnlyDictionary<string, Series>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return new Dictionary<string, Series>();

        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var user = _httpContextAccessor.GetAuthUser();
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        parameters.Add("SeriesKeys", keys);

        var sql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" IN @SeriesKeys AND {where};
            """;

        var series = await conn.QueryAsync<Series>(sql, parameters);
        return series.ToDictionary(s => s.Id);
    }
}

public sealed class LibraryByIdDataLoader : BatchDataLoader<string, Library>
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LibraryByIdDataLoader(
        ISqliteConnectionFactory connectionFactory,
        IHttpContextAccessor httpContextAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _connectionFactory = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<IReadOnlyDictionary<string, Library>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return new Dictionary<string, Library>();

        await using var conn = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var user = _httpContextAccessor.GetAuthUser();
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        parameters.Add("LibraryKeys", keys);

        var sql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE l."Id" IN @LibraryKeys AND {where};
            """;

        var libraries = await conn.QueryAsync<Library>(sql, parameters);
        return libraries.ToDictionary(l => l.Id);
    }
}

/// <summary>
/// AddDataLoader&lt;T&gt;() construye el loader con ActivatorUtilities, que busca el constructor por
/// reflexion; en Native AOT ese constructor no se conserva y el arranque falla. La fabrica explicita
/// deja la construccion en codigo compilado.
/// </summary>
internal static class CatalogDataLoaderRegistration
{
    public static IRequestExecutorBuilder AddCatalogDataLoaders(this IRequestExecutorBuilder builder) => builder
        .AddDataLoader(sp => Create(sp, (db, http, scheduler, options) => new MediaBySeriesDataLoader(db, http, scheduler, options)))
        .AddDataLoader(sp => Create(sp, (db, http, scheduler, options) => new SeriesByLibraryDataLoader(db, http, scheduler, options)))
        .AddDataLoader(sp => Create(sp, (db, http, scheduler, options) => new SeriesByIdDataLoader(db, http, scheduler, options)))
        .AddDataLoader(sp => Create(sp, (db, http, scheduler, options) => new LibraryByIdDataLoader(db, http, scheduler, options)));

    private static T Create<T>(
        IServiceProvider sp,
        Func<ISqliteConnectionFactory, IHttpContextAccessor, IBatchScheduler, DataLoaderOptions, T> create) =>
        create(
            sp.GetRequiredService<ISqliteConnectionFactory>(),
            sp.GetRequiredService<IHttpContextAccessor>(),
            sp.GetRequiredService<IBatchScheduler>(),
            sp.GetRequiredService<DataLoaderOptions>());
}

internal static class AuthUserAccessor
{
    public static AuthUser? GetAuthUser(this IHttpContextAccessor accessor) =>
        accessor.HttpContext?.Items[GraphQLConstants.AuthUserKey] as AuthUser;
}
