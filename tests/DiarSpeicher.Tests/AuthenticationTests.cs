using System.Text;
using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Security;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Tests;

/// <summary>
/// Covers the acceptance criteria of block B1 of the phase 1 closure plan: a wrong password,
/// a non-existent or expired API key, and a locked or deleted user must all produce 401,
/// while a server with no users keeps working anonymously for backwards compatibility.
/// </summary>
public sealed class AuthenticationTests : IDisposable
{
    private const string ProtectedPath = "/api/v2/media";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly IPasswordHasher _passwordHasher = new Pbkdf2PasswordHasher();

    public AuthenticationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CorrectPassword_Authenticates()
    {
        await SeedUserAsync("diar", "SuperSecret123");

        var context = await InvokeAsync(ConfigureBasic("diar", "SuperSecret123"));

        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var user = Assert.IsType<AuthUser>(context.Items["AuthUser"]);
        Assert.Equal("diar", user.Username);
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        await SeedUserAsync("diar", "SuperSecret123");

        var context = await InvokeAsync(ConfigureBasic("diar", "la-equivocada"));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Null(context.Items["AuthUser"]);
    }

    [Fact]
    public async Task EmptyPassword_IsRejected()
    {
        await SeedUserAsync("diar", "SuperSecret123");

        var context = await InvokeAsync(ConfigureBasic("diar", string.Empty));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiKey_IsRejected_AndDoesNotGrantAnonymousAccess()
    {
        await SeedUserAsync("diar", "SuperSecret123");

        var context = await InvokeAsync(ctx => ctx.Request.Headers["X-Auth-Key"] = "clave-inventada");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Null(context.Items["AuthUser"]);
    }

    [Fact]
    public async Task ValidApiKey_Authenticates()
    {
        var user = await SeedUserAsync("diar", "SuperSecret123");
        var key = await SeedApiKeyAsync(user.Id, expiresAt: null);

        var context = await InvokeAsync(ctx => ctx.Request.Headers["X-Auth-Key"] = key);

        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var authUser = Assert.IsType<AuthUser>(context.Items["AuthUser"]);
        Assert.Equal(user.Id, authUser.Id);
    }

    [Fact]
    public async Task ExpiredApiKey_IsRejected()
    {
        var user = await SeedUserAsync("diar", "SuperSecret123");
        var key = await SeedApiKeyAsync(user.Id, DateTimeOffset.UtcNow.AddDays(-1));

        var context = await InvokeAsync(ctx => ctx.Request.Headers["X-Auth-Key"] = key);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task LockedUser_IsRejected()
    {
        await SeedUserAsync("diar", "SuperSecret123", isLocked: true);

        var context = await InvokeAsync(ConfigureBasic("diar", "SuperSecret123"));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task DeletedUser_IsRejected()
    {
        await SeedUserAsync("diar", "SuperSecret123", deletedAt: DateTimeOffset.UtcNow);

        var context = await InvokeAsync(ConfigureBasic("diar", "SuperSecret123"));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task LockedUsersApiKey_IsRejected()
    {
        var user = await SeedUserAsync("diar", "SuperSecret123", isLocked: true);
        var key = await SeedApiKeyAsync(user.Id, expiresAt: null);

        var context = await InvokeAsync(ctx => ctx.Request.Headers["X-Auth-Key"] = key);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task ServerWithNoUsers_StillAllowsAnonymousOwner()
    {
        var context = await InvokeAsync(_ => { });

        var user = Assert.IsType<AuthUser>(context.Items["AuthUser"]);
        Assert.True(user.IsServerOwner);
        Assert.Equal("default-owner", user.Id);
    }

    [Fact]
    public async Task ClaimRequest_IsNotBlockedOnAClaimedServer()
    {
        await SeedUserAsync("diar", "SuperSecret123");

        var nextCalled = false;
        await InvokeAsync(
            ctx =>
            {
                ctx.Request.Method = HttpMethods.Post;
                ctx.Request.Path = "/api/v2/claim";
            },
            () => nextCalled = true);

        Assert.True(nextCalled);
    }

    [Fact]
    public void PasswordHasher_ProducesVerifiableSaltedHashes()
    {
        var first = _passwordHasher.Hash("SuperSecret123");
        var second = _passwordHasher.Hash("SuperSecret123");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("SuperSecret123", first, StringComparison.Ordinal);
        Assert.True(_passwordHasher.Verify("SuperSecret123", first));
        Assert.False(_passwordHasher.Verify("otra", first));
        Assert.False(_passwordHasher.Verify("SuperSecret123", null));
    }

    private static Action<HttpContext> ConfigureBasic(string username, string password) =>
        context =>
        {
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            context.Request.Headers.Authorization = $"Basic {raw}";
        };

    private async Task<HttpContext> InvokeAsync(Action<HttpContext> configure, Action? onNext = null)
    {
        await using var db = new DiarSpeicherDbContext(_options);

        var context = new DefaultHttpContext();
        context.Request.Path = ProtectedPath;
        configure(context);

        var middleware = new OpdsAuthMiddleware(_ =>
        {
            onNext?.Invoke();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db, _passwordHasher);

        return context;
    }

    private async Task<User> SeedUserAsync(
        string username,
        string password,
        bool isLocked = false,
        DateTimeOffset? deletedAt = null)
    {
        await using var db = new DiarSpeicherDbContext(_options);

        var user = new User
        {
            Username = username,
            HashedPassword = _passwordHasher.Hash(password),
            IsServerOwner = true,
            IsLocked = isLocked,
            DeletedAt = deletedAt
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    private async Task<string> SeedApiKeyAsync(string userId, DateTimeOffset? expiresAt)
    {
        await using var db = new DiarSpeicherDbContext(_options);

        var plaintext = ApiKeyGenerator.GenerateKey();
        db.ApiKeys.Add(new ApiKey
        {
            UserId = userId,
            Name = "test",
            KeyHash = ApiKeyGenerator.HashKey(plaintext),
            ExpiresAt = expiresAt
        });

        await db.SaveChangesAsync();

        return plaintext;
    }
}
