using System.Security.Claims;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Api.Middleware;

public class GatewayIdentityMiddleware
{
    private const string AuthUserKey = "AuthUser";
    private readonly RequestDelegate _next;

    public GatewayIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DiarSpeicherDbContext db)
    {
        var sub = context.Request.Headers["X-Auth-Sub"].LastOrDefault();

        if (string.IsNullOrWhiteSpace(sub) || IsAnonymousSubject(sub))
        {
            await _next(context);
            return;
        }

        var username = context.Request.Headers["X-Auth-User"].LastOrDefault();
        var roleHeader = context.Request.Headers["X-Auth-Role"].LastOrDefault() ?? "";

        var ageHeader = context.Request.Headers["X-Auth-Age"].LastOrDefault();

        var permsHeader = context.Request.Headers["X-Auth-Perms"].LastOrDefault() ?? "";

        var mirrored = await SyncMirrorAsync(
            db,
            sub,
            string.IsNullOrWhiteSpace(username) ? sub : username,
            context.RequestAborted);

        var authUser = BuildAuthUser(mirrored, roleHeader, permsHeader, ageHeader);

        context.Items[AuthUserKey] = authUser;
        context.User = BuildPrincipal(authUser);

        await _next(context);
    }

    /// <summary>
    /// Mirrors the gateway's user locally. Ownership is never taken from the request: it is
    /// granted once, to the first user the instance ever sees, and from then on only changes
    /// through the local database.
    /// </summary>
    private static async Task<User> SyncMirrorAsync(
        DiarSpeicherDbContext db,
        string id,
        string username,
        CancellationToken ct)
    {
        var existing = await db.Users
            .Include(u => u.AgeRestriction)
            .Include(u => u.ExcludedLibraries)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (existing is not null)
        {
            if (existing.Username != username)
            {
                existing.Username = username;
                await db.SaveChangesAsync(ct);
            }

            return existing;
        }

        var existingByUsername = await db.Users
            .Include(u => u.AgeRestriction)
            .Include(u => u.ExcludedLibraries)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (existingByUsername is not null)
        {
            var oldId = existingByUsername.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE UserPreferences SET UserId = {id} WHERE UserId = {oldId};
                UPDATE AgeRestrictions SET UserId = {id} WHERE UserId = {oldId};
                UPDATE LibraryExclusions SET UserId = {id} WHERE UserId = {oldId};
                UPDATE ReadingSessions SET UserId = {id} WHERE UserId = {oldId};
                UPDATE Users SET Id = {id} WHERE Id = {oldId};
            """, ct);

            existingByUsername.Id = id;
            return existingByUsername;
        }

        var isFirstUser = !await db.Users.AnyAsync(ct);

        var created = new User
        {
            Id = id,
            Username = username,
            IsServerOwner = isFirstUser
        };

        db.Users.Add(created);
        await db.SaveChangesAsync(ct);

        return created;
    }

    private static bool IsAnonymousSubject(string sub) =>
        sub.Equals("anonymous", StringComparison.OrdinalIgnoreCase);

    private static ClaimsPrincipal BuildPrincipal(AuthUser authUser)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, authUser.Id),
            new(ClaimTypes.Name, authUser.Username)
        };

        if (authUser.IsServerOwner)
        {
            claims.Add(new Claim("IsServerOwner", "true"));
        }

        foreach (var role in authUser.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "GatewayTrusted"));
    }

    private static AuthUser BuildAuthUser(User mirrored, string roleHeader, string permsHeader, string? ageHeader)
    {
        var authUser = new AuthUser
        {
            Id = mirrored.Id,
            Username = mirrored.Username,
            IsServerOwner = mirrored.IsServerOwner,
            AgeRestriction = mirrored.AgeRestriction?.Age,
            RestrictOnUnset = mirrored.AgeRestriction?.RestrictOnUnset ?? true,
            ExcludedLibraryIds = [.. mirrored.ExcludedLibraries.Select(e => e.LibraryId)]
        };

        foreach (var role in roleHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            authUser.Roles.Add(role);
        }

        foreach (var permission in permsHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            authUser.Permissions.Add(permission);
        }

        if (int.TryParse(ageHeader, out var age))
        {
            authUser.AgeRestriction = age;
        }

        return authUser;
    }
}

public static class GatewayIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayIdentity(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayIdentityMiddleware>();
}
