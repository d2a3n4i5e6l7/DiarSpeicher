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
        // Last, not first: the gateway clears the header in its proxy_forward snippet and then
        // injects the validated value in the plugin location. If nginx were to forward both
        // instead of keeping the last, reading the first would yield the cleared empty value
        // and turn every request anonymous.
        var sub = context.Request.Headers["X-Auth-Sub"].LastOrDefault();

        // A public route arrives with the header present and empty, and the gateway may also
        // send the literal "anonymous". Neither is an identity: mirroring either would create a
        // user row shared by every anonymous visitor.
        if (string.IsNullOrWhiteSpace(sub) || IsAnonymousSubject(sub))
        {
            await _next(context);
            return;
        }

        var username = context.Request.Headers["X-Auth-User"].LastOrDefault();
        var roleHeader = context.Request.Headers["X-Auth-Role"].LastOrDefault() ?? "";

        // "X-Auth-Age" is the name the gateway injects. Server ownership is deliberately not
        // taken from a header: the gateway sends none, and nginx only strips the headers it
        // names one by one, so any header it does not name reaches us straight from the client.
        var ageHeader = context.Request.Headers["X-Auth-Age"].LastOrDefault();

        var mirrored = await SyncMirrorAsync(
            db,
            sub,
            string.IsNullOrWhiteSpace(username) ? sub : username,
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
}

public static class GatewayIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayIdentity(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayIdentityMiddleware>();
}
