using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Api.GraphQL;
using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Core.Gateway;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Reading;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Storage;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using DiarSpeicher.Infrastructure.Komga;
using DiarSpeicher.Infrastructure.Metadata;
using Microsoft.AspNetCore.HttpOverrides;
using DiarSpeicher.Infrastructure.Opds;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;";

var connectionString = new SqliteConnectionStringBuilder(configuredConnection)
{
    ForeignKeys = true
}.ToString();

const int SqliteMaxBatchSize = 200;

builder.Services.AddDbContext<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString, sqlite => sqlite.MaxBatchSize(SqliteMaxBatchSize));
});

builder.Services.AddDbContextFactory<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString, sqlite => sqlite.MaxBatchSize(SqliteMaxBatchSize));
}, lifetime: ServiceLifetime.Scoped);

builder.Services.AddHttpContextAccessor();

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));

builder.Services.AddSingleton<ILinkPrefixProvider, HttpContextLinkPrefixProvider>();


builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<MangaBakaOptions>(builder.Configuration.GetSection(MangaBakaOptions.SectionName));
builder.Services.Configure<LibraryRootsOptions>(builder.Configuration.GetSection(LibraryRootsOptions.SectionName));
builder.Services.Configure<TrashOptions>(builder.Configuration.GetSection(TrashOptions.SectionName));
builder.Services.AddSingleton<ITrashService, TrashService>();
builder.Services.AddHostedService<TrashPurgeService>();
builder.Services.AddHostedService<ScanRecoveryService>();
builder.Services.AddSingleton<IFolderIndex, FolderIndex>();
builder.Services.AddHostedService<FolderIndexStartupService>();
builder.Services.PostConfigure<StorageOptions>(ApplyUploadEnvironmentOverrides);
builder.Services.AddOptions<MangaBakaOptions>().PostConfigure<IOptions<StorageOptions>>(ApplyMangaBakaStorageRoot);
builder.Services.AddSingleton<IPageCache, DiskPageCache>();

builder.Services.AddSingleton<IEpubProfileProvider, DiarSpeicher.Api.Services.HttpEpubProfileProvider>();
builder.Services.AddSingleton<IBookProcessor, ZipBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, RarBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, EpubBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, PdfBookProcessor>();
builder.Services.AddSingleton<CompositeBookProcessor>();

builder.Services.AddSingleton<ICompositeBookProcessor>(sp => new CachingBookProcessor(
    sp.GetRequiredService<CompositeBookProcessor>(),
    sp.GetRequiredService<IPageCache>()));

builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();

builder.Services.AddScoped<IEpubPageMapStore, EpubPageMapStore>();
builder.Services.AddScoped<IReadingProgress, ReadingProgress>();

builder.Services.AddScoped<IOpdsService, OpdsService>();
builder.Services.AddScoped<IOpdsV2Service, OpdsV2Service>();
builder.Services.AddScoped<IKomgaService, KomgaService>();

builder.Services.AddScoped<IKoReaderService, KoReaderService>();
builder.Services.AddScoped<IKoboService, KoboService>();
builder.Services.AddScoped(sp => new DiarSpeicherServiceOptions
{
    LibraryRoots = sp.GetService<IOptions<LibraryRootsOptions>>(),
    Trash = sp.GetService<ITrashService>()
});
builder.Services.AddScoped<IDiarSpeicherService, DiarSpeicherService>();

builder.Services.AddSingleton<IDirectoryScanner, DirectoryScanner>();
builder.Services.AddSingleton<IArchiveConversionService, ArchiveConversionService>();

builder.Services.AddSingleton<IMangaBakaCatalog, MangaBakaCatalog>();
builder.Services.AddSingleton<IMangaBakaIngestService, MangaBakaIngestService>();
builder.Services.AddScoped<ISeriesMetadataMatcher, SeriesMetadataMatcher>();
builder.Services.AddScoped<ILibraryScannerService, LibraryScannerService>();
builder.Services.AddSingleton<IScannerQueue, ScannerQueue>();
builder.Services.AddHostedService<ScanBackgroundService>();
builder.Services.AddHostedService<LibraryWatcherService>();
builder.Services.AddHostedService<DatabaseBackupService>();

builder.Services.AddSingleton<ScanProgressHub>();
builder.Services.AddSingleton<IScanProgressHub>(sp => sp.GetRequiredService<ScanProgressHub>());
builder.Services.AddScoped<GraphQLScanProgressPublisher>();
builder.Services.AddScoped<IScanProgressPublisher>(sp => new CompositeScanProgressPublisher(
    [sp.GetRequiredService<GraphQLScanProgressPublisher>(), sp.GetRequiredService<ScanProgressHub>()],
    sp.GetRequiredService<ILogger<CompositeScanProgressPublisher>>()));
builder.Services.AddScoped<AuthUserResolver>();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddSubscriptionType<Subscription>()
    .AddTypeExtension<SeriesResolvers>()
    .AddTypeExtension<MediaResolvers>()
    .AddTypeExtension<LibraryResolvers>()
    .AddTypeExtension<MangaBakaQueries>()
    .AddDataLoader<MediaBySeriesDataLoader>()
    .AddDataLoader<SeriesByLibraryDataLoader>()
    .AddDataLoader<SeriesByIdDataLoader>()
    .AddDataLoader<LibraryByIdDataLoader>()
    .AddInMemorySubscriptions()
    .AddUploadType()
    .AddFiltering()
    .AddSorting()
    .AddProjections()
    .ModifyRequestOptions(options => options.IncludeExceptionDetails = builder.Environment.IsDevelopment());

var app = builder.Build();

EnsureDataDirectories(app.Services, connectionString);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();
    await db.Database.MigrateAsync();
    await db.InitializeSqliteWalAsync();
    await db.BackfillSortNamesAsync();
}

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeaders);

var gateway = app.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
if (!string.IsNullOrWhiteSpace(gateway.PathBase))
{
    app.UsePathBase("/" + gateway.PathBase.Trim('/'));
}

app.UseGatewayIdentity();
app.UseOpdsAuth();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "DiarSpeicher",
    version = "1.0.0",
    database = "SQLite (WAL)"
}));

app.UseWebSockets();

app.MapGraphQL();
app.MapOpdsEndpoints();
app.MapOpdsV2Endpoints();
app.MapKomgaEndpoints();
app.MapKoReaderEndpoints();
app.MapKoboEndpoints();
app.MapDiarSpeicherEndpoints();
app.MapMetadataEndpoints();
app.MapFilesystemEndpoints();
app.MapTrashEndpoints();
app.MapTusEndpoints();

await app.RunAsync();

static void EnsureDataDirectories(IServiceProvider services, string connectionString)
{
    var storage = services.GetRequiredService<IOptions<StorageOptions>>().Value;

    var directories = new List<string>
    {
        Path.GetFullPath(storage.RootPath),
        storage.ResolveThumbnailsPath(),
        storage.ResolvePageCachePath(),
        storage.ResolveBackupPath(),
        storage.ResolveUploadsPath(),
        Path.Combine(Path.GetFullPath(storage.RootPath), "manga_database"),
    };

    // Una base en memoria no tiene directorio que crear.
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (!string.IsNullOrWhiteSpace(dataSource)
        && !dataSource.Equals(":memory:", StringComparison.Ordinal))
    {
        var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(databaseDirectory))
        {
            directories.Add(databaseDirectory);
        }
    }

    foreach (var directory in directories)
    {
        Directory.CreateDirectory(directory);
    }
}

static void ApplyMangaBakaStorageRoot(MangaBakaOptions options, IOptions<StorageOptions> storageOptions)
{
    if (!string.IsNullOrWhiteSpace(options.DatabasePath)) return;

    options.DatabasePath = Path.Combine(Path.GetFullPath(storageOptions.Value.RootPath), "manga_database");
}

static void ApplyUploadEnvironmentOverrides(StorageOptions options)
{
    var enableUpload = Environment.GetEnvironmentVariable("DIAR_ENABLE_UPLOAD");
    if (bool.TryParse(enableUpload, out var parsedEnableUpload))
    {
        options.Upload.EnableUpload = parsedEnableUpload;
    }

    var maxFileSize = Environment.GetEnvironmentVariable("DIAR_MAX_FILE_UPLOAD_SIZE");
    if (long.TryParse(maxFileSize, out var parsedMaxFileSize) && parsedMaxFileSize > 0)
    {
        options.Upload.MaxFileUploadSize = parsedMaxFileSize;
    }

    var allowedExtensions = Environment.GetEnvironmentVariable("DIAR_ALLOWED_EXTENSIONS");
    if (!string.IsNullOrWhiteSpace(allowedExtensions))
    {
        var parsed = allowedExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parsed.Count > 0)
        {
            options.Upload.AllowedExtensions = parsed;
        }
    }
}
