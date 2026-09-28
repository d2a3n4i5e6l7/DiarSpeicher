using System.Security.Claims;
using Dapper;
using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class GatewayIdentityTests : IDisposable
{
    private readonly TestSqliteDatabase _db;

    public GatewayIdentityTests()
    {
        _db = new TestSqliteDatabase();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

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

        await middleware.InvokeAsync(context, _db.Factory);

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

        var users = _db.Connection.Query<User>("SELECT * FROM \"Users\"");
        Assert.Empty(users);
    }

    [Fact]
    public async Task FirstUserBecomesServerOwner()
    {
        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        var user = Resolved(context);
        Assert.NotNull(user);
        Assert.True(user.IsServerOwner);
        Assert.Equal("diar", user.Username);

        var mirrored = Assert.Single(_db.Connection.Query<User>("SELECT * FROM \"Users\""));
        Assert.Equal("u1", mirrored.Id);
        Assert.True(mirrored.IsServerOwner);
    }

    [Fact]
    public async Task OnlyTheFirstUserBecomesServerOwner()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"));
        var context = await InvokeAsync(("X-Auth-Sub", "u2"));

        Assert.False(Resolved(context)!.IsServerOwner);

        var count = _db.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Users\"");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RepeatedRequestReusesTheMirrorRow()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        var users = _db.Connection.Query<User>("SELECT * FROM \"Users\"");
        Assert.Single(users);
    }

    [Fact]
    public async Task RenameInTheGatewayUpdatesTheMirror()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "viejo"));
        await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "nuevo"));

        var mirrored = Assert.Single(_db.Connection.Query<User>("SELECT * FROM \"Users\""));
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
        _db.Connection.Execute(
            "INSERT INTO \"Users\" (\"Id\", \"Username\", \"CreatedAt\") VALUES ('u1', 'diar', @Now);",
            new { Now = DateTimeOffset.UtcNow });
        _db.Connection.Execute(
            "INSERT INTO \"AgeRestrictions\" (\"UserId\", \"Age\", \"RestrictOnUnset\") VALUES ('u1', 7, 0);");

        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        var user = Resolved(context)!;
        Assert.Equal(7, user.AgeRestriction);
        Assert.False(user.RestrictOnUnset);
    }

    [Fact]
    public async Task ExistingMirrorKeepsItsLibraryExclusions()
    {
        _db.Connection.Execute(
            "INSERT INTO \"LibraryConfigs\" (\"Id\") VALUES (1);");
        _db.Connection.Execute(
            "INSERT INTO \"Libraries\" (\"Id\", \"Name\", \"Path\", \"ConfigId\", \"CreatedAt\") VALUES ('lib-1', 'Comics', '/comics', 1, @Now);",
            new { Now = DateTimeOffset.UtcNow });
        _db.Connection.Execute(
            "INSERT INTO \"Users\" (\"Id\", \"Username\", \"CreatedAt\") VALUES ('u1', 'diar', @Now);",
            new { Now = DateTimeOffset.UtcNow });
        _db.Connection.Execute(
            "INSERT INTO \"LibraryExclusions\" (\"LibraryId\", \"UserId\") VALUES ('lib-1', 'u1');");

        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-User", "diar"));

        Assert.Contains("lib-1", Resolved(context)!.ExcludedLibraryIds);
    }

    [Fact]
    public async Task ServerOwnerHeaderDoesNotPromoteAnyone()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"));
        await InvokeAsync(("X-Auth-Sub", "u2"));

        var context = await InvokeAsync(("X-Auth-Sub", "u2"), ("X-Auth-Server-Owner", "true"));

        Assert.False(Resolved(context)!.IsServerOwner);

        var mirrored = _db.Connection.QueryFirst<User>("SELECT * FROM \"Users\" WHERE \"Id\" = 'u2'");
        Assert.False(mirrored.IsServerOwner);
    }

    [Fact]
    public async Task ServerOwnerHeaderDoesNotDemoteTheOwner()
    {
        await InvokeAsync(("X-Auth-Sub", "u1"));

        var context = await InvokeAsync(("X-Auth-Sub", "u1"), ("X-Auth-Server-Owner", "false"));

        Assert.True(Resolved(context)!.IsServerOwner);

        var mirrored = _db.Connection.QueryFirst<User>("SELECT * FROM \"Users\" WHERE \"Id\" = 'u1'");
        Assert.True(mirrored.IsServerOwner);
    }

    [Fact]
    public async Task AnonymousSubjectResolvesNoIdentity()
    {
        var context = await InvokeAsync(("X-Auth-Sub", "anonymous"));

        Assert.Null(Resolved(context));

        var users = _db.Connection.Query<User>("SELECT * FROM \"Users\"");
        Assert.Empty(users);
    }

    [Fact]
    public async Task DuplicatedSubjectHeaderTakesTheInjectedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v2/series";
        context.Request.Headers["X-Auth-Sub"] = new[] { "", "u1" };

        var middleware = new GatewayIdentityMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _db.Factory);

        Assert.Equal("u1", Resolved(context)!.Id);
    }

    [Fact]
    public async Task PermissionsComeFromTheHeader()
    {
        var context = await InvokeAsync(
            ("X-Auth-Sub", "u1"),
            ("X-Auth-Perms", "FileUpload, AccessKoboSync"));

        var user = Resolved(context)!;
        Assert.Contains("FileUpload", user.Permissions);
        Assert.Contains("AccessKoboSync", user.Permissions);
        Assert.True(user.HasPermission(Permissions.FileUpload));
        Assert.True(user.HasPermission(Permissions.AccessKoboSync));
    }

    [Fact]
    public async Task ServerOwnerIsExemptFromPermissions()
    {
        var context = await InvokeAsync(("X-Auth-Sub", "u1"));

        var user = Resolved(context)!;
        Assert.True(user.IsServerOwner);
        Assert.Empty(user.Permissions);
        Assert.True(user.HasPermission(Permissions.FileUpload));
    }

    [Fact]
    public async Task MissingPermissionIsDenied()
    {
        _db.Connection.Execute(
            "INSERT INTO \"Users\" (\"Id\", \"Username\", \"IsServerOwner\", \"CreatedAt\") VALUES ('owner', 'owner', 1, @Now);",
            new { Now = DateTimeOffset.UtcNow });

        var context = await InvokeAsync(
            ("X-Auth-Sub", "u2"),
            ("X-Auth-Perms", "AccessKoreaderSync"));

        var user = Resolved(context)!;
        Assert.False(user.IsServerOwner);
        Assert.True(user.HasPermission(Permissions.AccessKoreaderSync));
        Assert.False(user.HasPermission(Permissions.FileUpload));
    }

    [Fact]
    public async Task WildcardGrantsEveryPermission()
    {
        _db.Connection.Execute(
            "INSERT INTO \"Users\" (\"Id\", \"Username\", \"IsServerOwner\", \"CreatedAt\") VALUES ('owner', 'owner', 1, @Now);",
            new { Now = DateTimeOffset.UtcNow });

        var context = await InvokeAsync(("X-Auth-Sub", "u2"), ("X-Auth-Perms", "*"));

        var user = Resolved(context)!;
        Assert.False(user.IsServerOwner);
        Assert.True(user.HasPermission(Permissions.FileUpload));
        Assert.True(user.HasPermission(Permissions.ManageLibrary));
    }

    [Fact]
    public async Task DuplicatedPermissionsHeaderTakesTheInjectedValue()
    {
        _db.Connection.Execute(
            "INSERT INTO \"Users\" (\"Id\", \"Username\", \"IsServerOwner\", \"CreatedAt\") VALUES ('owner', 'owner', 1, @Now);",
            new { Now = DateTimeOffset.UtcNow });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v2/series";
        context.Request.Headers["X-Auth-Sub"] = new[] { "", "u2" };
        context.Request.Headers["X-Auth-Perms"] = new[] { "", "FileUpload" };

        var middleware = new GatewayIdentityMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _db.Factory);

        Assert.True(Resolved(context)!.HasPermission(Permissions.FileUpload));
    }
}
