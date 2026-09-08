using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
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

await app.RunAsync();
