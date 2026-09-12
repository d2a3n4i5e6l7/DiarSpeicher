using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Api.GraphQL;
using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Core.Gateway;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Storage;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using DiarSpeicher.Infrastructure.Komga;
using DiarSpeicher.Infrastructure.Metadata;
using Microsoft.AspNetCore.HttpOverrides;
using DiarSpeicher.Infrastructure.Opds;
using DiarSpeicher.Infrastructure.StumpV2;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;";

const int SqliteMaxBatchSize = 200;

builder.Services.AddDbContext<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString, sqlite => sqlite.MaxBatchSize(SqliteMaxBatchSize));
});

// DataLoaders resolve batches concurrently, and a DbContext is not thread-safe, so the
// GraphQL layer takes a short-lived context per batch from the factory.
builder.Services.AddDbContextFactory<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString, sqlite => sqlite.MaxBatchSize(SqliteMaxBatchSize));
}, lifetime: ServiceLifetime.Scoped);

builder.Services.AddHttpContextAccessor();

// Contrato con el Gateway: prefijo de montaje y rutas que sirve sin identidad.
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));

// Los enlaces de los feeds llevan el prefijo del montaje. Vive en Api porque lee el
// HttpContext; Infrastructure solo conoce la interfaz.
builder.Services.AddSingleton<ILinkPrefixProvider, HttpContextLinkPrefixProvider>();


// Storage locations & page cache
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<MangaBakaOptions>(builder.Configuration.GetSection(MangaBakaOptions.SectionName));
builder.Services.PostConfigure<StorageOptions>(ApplyUploadEnvironmentOverrides);
builder.Services.AddOptions<MangaBakaOptions>().PostConfigure<IOptions<StorageOptions>>(ApplyMangaBakaStorageRoot);
builder.Services.AddSingleton<IPageCache, DiskPageCache>();

// Book Processing & Extraction
builder.Services.AddSingleton<IBookProcessor, ZipBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, RarBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, EpubBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, PdfBookProcessor>();
builder.Services.AddSingleton<CompositeBookProcessor>();

// Page extraction is served through the disk cache, so every consumer (OPDS, Stump v2,
// thumbnails) skips repeated decompression of the same page.
builder.Services.AddSingleton<ICompositeBookProcessor>(sp => new CachingBookProcessor(
    sp.GetRequiredService<CompositeBookProcessor>(),
    sp.GetRequiredService<IPageCache>()));

builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();


// OPDS v1.2, v2.0 & Komga Services
builder.Services.AddScoped<IOpdsService, OpdsService>();
builder.Services.AddScoped<IOpdsV2Service, OpdsV2Service>();
builder.Services.AddScoped<IKomgaService, KomgaService>();

// E-Reader Sync (KOReader & Kobo) and Stump API v2 Services
builder.Services.AddScoped<IKoReaderService, KoReaderService>();
builder.Services.AddScoped<IKoboService, KoboService>();
builder.Services.AddScoped<IStumpV2Service, StumpV2Service>();

// Filesystem Scanner & Background Worker
builder.Services.AddSingleton<IDirectoryScanner, DirectoryScanner>();
builder.Services.AddSingleton<IArchiveConversionService, ArchiveConversionService>();

// Catalogo externo de MangaBaka. El volcado es un fichero de solo lectura en disco, asi que
// el catalogo es singleton; el emparejador toca la base de datos y va por peticion.
builder.Services.AddSingleton<IMangaBakaCatalog, MangaBakaCatalog>();
builder.Services.AddSingleton<IMangaBakaIngestService, MangaBakaIngestService>();
builder.Services.AddScoped<ISeriesMetadataMatcher, SeriesMetadataMatcher>();
builder.Services.AddScoped<ILibraryScannerService, LibraryScannerService>();
builder.Services.AddSingleton<IScannerQueue, ScannerQueue>();
builder.Services.AddHostedService<ScanBackgroundService>();
builder.Services.AddHostedService<LibraryWatcherService>();
builder.Services.AddHostedService<DatabaseBackupService>();

builder.Services.AddScoped<IScanProgressPublisher, GraphQLScanProgressPublisher>();
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

// Los directorios de datos se crean en cada arranque, no en la imagen: el volumen se monta
// encima de lo que traiga la imagen y tapa los directorios que se crearon al construirla.
// La imagen final es distroless, asi que tampoco hay shell con la que crearlos antes de
// arrancar el proceso.
EnsureDataDirectories(app.Services, connectionString);

// Inicializar base de datos y WAL
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();
    await db.Database.MigrateAsync();
    await db.InitializeSqliteWalAsync();
}

// El salto gateway -> backend va en claro, asi que el esquema real lo trae la cabecera. Sin
// vaciar las listas solo se confiaria en loopback y el gateway es otro contenedor.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownNetworks.Clear();
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
app.MapStumpV2Endpoints();
app.MapMetadataEndpoints();
app.MapTusEndpoints();

await app.RunAsync();

/// <summary>
/// Crea el directorio de la base de datos y los de los datos generados. SQLite no crea el
/// directorio que contiene el fichero, de modo que la migracion inicial falla si no existe,
/// y el resto se crean aqui para que un fallo de permisos salte al arrancar y no en la
/// primera peticion que toque disco.
/// </summary>
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

/// <summary>
/// Sin ruta configurada, el volcado cuelga del volumen de datos. El directorio de trabajo
/// del proceso no vale: en el contenedor es /home/nonroot, que ni es escribible ni persiste
/// entre despliegues, y son 3,5 GB que no se quieren volver a descargar.
/// </summary>
static void ApplyMangaBakaStorageRoot(MangaBakaOptions options, IOptions<StorageOptions> storageOptions)
{
    if (!string.IsNullOrWhiteSpace(options.DatabasePath)) return;

    options.DatabasePath = Path.Combine(Path.GetFullPath(storageOptions.Value.RootPath), "manga_database");
}

/// <summary>
/// The DIAR_* variables documented for uploads are applied after the "Storage" section is
/// bound, so an explicitly set variable always wins over appsettings.
/// </summary>
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
