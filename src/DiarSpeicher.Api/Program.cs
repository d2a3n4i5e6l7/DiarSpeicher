using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using DiarSpeicher.Infrastructure.Komga;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=diarspeicher.db;Cache=Shared;Mode=RWC;";

builder.Services.AddDbContext<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Book Processing & Extraction
builder.Services.AddSingleton<IBookProcessor, ZipBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, RarBookProcessor>();
builder.Services.AddSingleton<IBookProcessor, EpubBookProcessor>();
builder.Services.AddSingleton<ICompositeBookProcessor, CompositeBookProcessor>();
builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();

// OPDS v1.2 & Media Streaming Service
builder.Services.AddScoped<IOpdsService, OpdsService>();
builder.Services.AddScoped<IOpdsV2Service, OpdsV2Service>();
builder.Services.AddScoped<IKomgaService, KomgaService>();

// Filesystem Scanner & Background Worker
builder.Services.AddSingleton<IDirectoryScanner, DirectoryScanner>();
builder.Services.AddScoped<ILibraryScannerService, LibraryScannerService>();
builder.Services.AddSingleton<IScannerQueue, ScannerQueue>();
builder.Services.AddHostedService<ScanBackgroundService>();

var app = builder.Build();

// Inicializar base de datos y WAL
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();
    await db.Database.EnsureCreatedAsync();
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

app.MapOpdsEndpoints();
app.MapOpdsV2Endpoints();
app.MapKomgaEndpoints();

await app.RunAsync();

