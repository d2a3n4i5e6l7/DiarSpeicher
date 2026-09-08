using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Api.GraphQL;
using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Core.Security;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Storage;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using DiarSpeicher.Infrastructure.Identity;
using DiarSpeicher.Infrastructure.Komga;
using DiarSpeicher.Infrastructure.Opds;
using DiarSpeicher.Infrastructure.StumpV2;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;";

builder.Services.AddDbContext<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// DataLoaders resolve batches concurrently, and a DbContext is not thread-safe, so the
// GraphQL layer takes a short-lived context per batch from the factory.
builder.Services.AddDbContextFactory<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString);
}, lifetime: ServiceLifetime.Scoped);

builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

// Storage locations & page cache
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.PostConfigure<StorageOptions>(ApplyUploadEnvironmentOverrides);
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

builder.Services.AddScoped<IIdentityService, IdentityService>();

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
builder.Services.AddScoped<ILibraryScannerService, LibraryScannerService>();
builder.Services.AddSingleton<IScannerQueue, ScannerQueue>();
builder.Services.AddHostedService<ScanBackgroundService>();
builder.Services.AddHostedService<LibraryWatcherService>();

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

// Inicializar base de datos y WAL
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();
    await db.Database.MigrateAsync();
    await db.InitializeSqliteWalAsync();
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

app.MapPost("/api/libraries/{id}/scan", async (string id, IScannerQueue queue) =>
{
    await queue.QueueScanAsync(new ScanRequest(id));
    return Results.Accepted($"/api/libraries/{id}/scan", new
    {
        message = "Library scan queued successfully",
        libraryId = id
    });
});

app.UseWebSockets();

app.MapGraphQL();
app.MapIdentityEndpoints();
app.MapOpdsEndpoints();
app.MapOpdsV2Endpoints();
app.MapKomgaEndpoints();
app.MapKoReaderEndpoints();
app.MapKoboEndpoints();
app.MapStumpV2Endpoints();

await app.RunAsync();

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
