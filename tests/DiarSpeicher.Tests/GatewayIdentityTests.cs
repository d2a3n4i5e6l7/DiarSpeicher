using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Tests;

public sealed class GatewayIdentityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    public GatewayIdentityTests()
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

    private DiarSpeicherDbContext NewContext() => new(_options);

    private async Task<HttpContext> InvokeAsync(params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v2/series";
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        var nextCalled = false;
        var middleware = new GatewayIdentityMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await using var db = NewContext();
        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
        return context;
    }

    private static AuthUser? Resolved(HttpContext context) =>
        context.Items.TryGetValue("AuthUser", out var value) ? value as AuthUser : null;

    [Fact]
    public async Task WithoutHeaders_ResolvesNoIdentity()
    {
        var context = await InvokeAsync();

        Assert.Null(Resolved(context));

        await using var db = NewContext();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task FirstUserBecomesServerOwner()
    {
        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        var user = Resolved(context);
        Assert.NotNull(user);
        Assert.True(user.IsServerOwner);
        Assert.Equal("diar", user.Username);

        await using var db = NewContext();
        var mirrored = Assert.Single(await db.Users.ToListAsync());
        Assert.Equal("u1", mirrored.Id);
        Assert.True(mirrored.IsServerOwner);
    }

    [Fact]
    public async Task OnlyTheFirstUserBecomesServerOwner()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"));
        var context = await InvokeAsync(("X-Auth-Sub", "u2"));

        Assert.False(Resolved(context)!.IsServerOwner);

        await using var db = NewContext();
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Fact]
    public async Task RepeatedRequestReusesTheMirrorRow()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        await using var db = NewContext();
        Assert.Single(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task RenameInTheGatewayUpdatesTheMirror()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "viejo"));
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "nuevo"));

        await using var db = NewContext();
        var mirrored = Assert.Single(await db.Users.ToListAsync());
        Assert.Equal("nuevo", mirrored.Username);
    }

    [Fact]
    public async Task SubIsUsedAsUsernameWhenTheGatewaySendsNone()
    {
        var context = await InvokeAsync(("X-Auth-Sub", "u1"));

        Assert.Equal("u1", Resolved(context)!.Username);
    }

    [Fact]
    public async Task RolesAndAgeRestrictionComeFromHeaders()
    {
        var context = await InvokeAsync(
            ("X-Auth-Sub", "u1"),
            ("X-Auth-Role", "reader, editor"),
            ("X-Auth-Age", "13"));

        var user = Resolved(context)!;
        Assert.Equal(13, user.AgeRestriction);
        Assert.Contains("reader", user.Roles);
        Assert.Contains("editor", user.Roles);
    }

    [Fact]
    public async Task ExistingMirrorKeepsItsAgeRestriction()
    {
        await using (var seed = NewContext())
        {
            seed.Users.Add(new User
            {
                Id = "u1",
                Username = "diar",
                AgeRestriction = new AgeRestriction { Age = 7, RestrictOnUnset = false }
            });
            await seed.SaveChangesAsync();
        }

        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        var user = Resolved(context)!;
        Assert.Equal(7, user.AgeRestriction);
        Assert.False(user.RestrictOnUnset);
    }

    [Fact]
    public async Task ExistingMirrorKeepsItsLibraryExclusions()
    {
        await using (var seed = NewContext())
        {
            seed.Libraries.Add(new Library
            {
                Id = "lib-1",
                Name = "Comics",
                Path = "/comics",
                Config = new LibraryConfig()
            });
            seed.Users.Add(new User { Id = "u1", Username = "diar" });
            await seed.SaveChangesAsync();

            seed.LibraryExclusions.Add(new LibraryExclusion { LibraryId = "lib-1", UserId = "u1" });
            await seed.SaveChangesAsync();
        }

        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        Assert.Contains("lib-1", Resolved(context)!.ExcludedLibraryIds);
    }

    /// <summary>
    /// nginx only strips the X-Auth-* headers it names one by one, and X-Auth-Server-Owner is
    /// not one of them, so it arrives straight from the client. Honouring it would let any
    /// authenticated user promote themselves permanently.
    /// </summary>
    [Fact]
    public async Task ServerOwnerHeaderDoesNotPromoteAnyone()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"));
        await InvokeAsync(("X-Auth-Sub", "u2"));

        var context = await InvokeAsync(("X-Auth-Sub", "u2"), ("X-Auth-Server-Owner", "true"));

        Assert.False(Resolved(context)!.IsServerOwner);

        await using var db = NewContext();
        var mirrored = await db.Users.FirstAsync(u => u.Id == "u2");
        Assert.False(mirrored.IsServerOwner);
    }

    /// <summary>
    /// Nor may it demote the owner, which is the same hole read the other way round.
    /// </summary>
    [Fact]
    public async Task ServerOwnerHeaderDoesNotDemoteTheOwner()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"));

        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-Server-Owner", "false"));

        Assert.True(Resolved(context)!.IsServerOwner);

        await using var db = NewContext();
        var mirrored = await db.Users.FirstAsync(u => u.Id == "u1");
        Assert.True(mirrored.IsServerOwner);
    }

    /// <summary>
    /// The gateway may send the literal "anonymous" on a public route. Mirroring it would
    /// create one user row shared by every anonymous visitor, and on a fresh instance that row
    /// would take server ownership.
    /// </summary>
    [Fact]
    public async Task AnonymousSubjectResolvesNoIdentity()
    {
        var context = await InvokeAsync(("X-Auth-Sub", "anonymous"));

        Assert.Null(Resolved(context));

        await using var db = NewContext();
        Assert.Empty(await db.Users.ToListAsync());
    }

    /// <summary>
    /// The gateway clears the header in its proxy_forward snippet and injects the validated
    /// value afterwards. Should nginx forward both instead of keeping the last, the identity is
    /// the last value, not the cleared one.
    /// </summary>
    [Fact]
    public async Task DuplicatedSubjectHeaderTakesTheInjectedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v2/series";
        context.Request.Headers["X-Auth-Sub"] = new[] { "", "u1" };

        var middleware = new GatewayIdentityMiddleware(_ => Task.CompletedTask);

        await using var db = NewContext();
        await middleware.InvokeAsync(context, db);

        Assert.Equal("u1", Resolved(context)!.Id);
    }
}
