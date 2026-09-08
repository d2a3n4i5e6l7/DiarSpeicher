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
        var sub = context.Request.Headers["X-Auth-Sub"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sub))
        {
            await _next(context);
            return;
        }

        var username = context.Request.Headers["X-Auth-User"].FirstOrDefault();
        var roleHeader = context.Request.Headers["X-Auth-Role"].FirstOrDefault() ?? "";
        var isOwnerHeader = context.Request.Headers["X-Auth-Server-Owner"].FirstOrDefault() ?? "";
        var ageHeader = context.Request.Headers["X-Auth-Age-Restriction"].FirstOrDefault();

        var mirrored = await SyncMirrorAsync(
            db,
            sub,
            string.IsNullOrWhiteSpace(username) ? sub : username,
            bool.TryParse(isOwnerHeader, out var isOwner) && isOwner,
            context.RequestAborted);

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

        if (int.TryParse(ageHeader, out var age))
        {
            authUser.AgeRestriction = age;
        }

        context.Items[AuthUserKey] = authUser;
        context.User = BuildPrincipal(authUser);

        await _next(context);
    }

    private static async Task<User> SyncMirrorAsync(
        DiarSpeicherDbContext db,
        string id,
        string username,
        bool isServerOwner,
        CancellationToken ct)
    {
        var existing = await db.Users
            .Include(u => u.AgeRestriction)
            .Include(u => u.ExcludedLibraries)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (existing is not null)
        {
            if (existing.Username != username || existing.IsServerOwner != isServerOwner)
            {
                existing.Username = username;
                existing.IsServerOwner = isServerOwner;
                await db.SaveChangesAsync(ct);
            }

            return existing;
        }

        var isFirstUser = !await db.Users.AnyAsync(ct);

        var created = new User
        {
            Id = id,
            Username = username,
            IsServerOwner = isServerOwner || isFirstUser
        };

        db.Users.Add(created);
        await db.SaveChangesAsync(ct);

        return created;
    }

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
}

public static class GatewayIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayIdentity(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayIdentityMiddleware>();
}
