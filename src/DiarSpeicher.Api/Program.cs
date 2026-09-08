using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=diarspeicher.db;Cache=Shared;Mode=RWC;";

builder.Services.AddDbContext<DiarSpeicherDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

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

await app.RunAsync();
