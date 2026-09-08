using System.Text;
using System.Text.RegularExpressions;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Api.Middleware;

public partial class OpdsAuthMiddleware
{
    private const string AuthUserKey = "AuthUser";
    private readonly RequestDelegate _next;
    private static readonly Regex ApiKeyPathRegex = MyRegex();

    public OpdsAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DiarSpeicherDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/opds", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var apiKey = ExtractApiKey(context, path);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            context.Items["OpdsApiKey"] = apiKey;
        }

        if (context.Items.ContainsKey(AuthUserKey))
        {
            await _next(context);
            return;
        }

        var ct = context.RequestAborted;
        if (await TryBasicAuthAsync(context, db, ct))
        {
            await _next(context);
            return;
        }

        if (await TryApiKeyAuthAsync(context, db, apiKey, ct))
        {
            await _next(context);
            return;
        }

        var hasUsers = await db.Users.AnyAsync(ct);
        if (!hasUsers)
        {
            context.Items[AuthUserKey] = new AuthUser
            {
                Id = "default-owner",
                Username = "admin",
                IsServerOwner = true
            };
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"DiarSpeicher OPDS\"";
    }

    private static string? ExtractApiKey(HttpContext context, string path)
    {
        var match = ApiKeyPathRegex.Match(path);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        if (context.Request.Query.TryGetValue("api_key", out var qKey) && !string.IsNullOrWhiteSpace(qKey))
        {
            return qKey.ToString();
        }

        if (context.Request.Headers.TryGetValue("X-Auth-Key", out var hKey) && !string.IsNullOrWhiteSpace(hKey))
        {
            return hKey.ToString();
        }

        return null;
    }

    private static async Task<bool> TryBasicAuthAsync(HttpContext context, DiarSpeicherDbContext db, CancellationToken ct)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var base64 = authHeader[6..].Trim();
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var separatorIndex = credentials.IndexOf(':');
            var username = separatorIndex >= 0 ? credentials[..separatorIndex] : credentials;

            var dbUser = await db.Users
                .Include(u => u.AgeRestriction)
                .Include(u => u.ExcludedLibraries)
                .FirstOrDefaultAsync(u => u.Username == username || u.Id == username, ct);

            if (dbUser == null) return false;

            context.Items[AuthUserKey] = new AuthUser
            {
                Id = dbUser.Id,
                Username = dbUser.Username,
                IsServerOwner = dbUser.IsServerOwner,
                AgeRestriction = dbUser.AgeRestriction?.Age,
                RestrictOnUnset = dbUser.AgeRestriction?.RestrictOnUnset ?? true,
                ExcludedLibraryIds = [.. dbUser.ExcludedLibraries.Select(e => e.LibraryId)]
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryApiKeyAuthAsync(HttpContext context, DiarSpeicherDbContext db, string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        var dbUser = await db.Users
            .Include(u => u.AgeRestriction)
            .Include(u => u.ExcludedLibraries)
            .FirstOrDefaultAsync(u => u.Username == apiKey || u.Id == apiKey, ct);

        context.Items[AuthUserKey] = dbUser != null
            ? new AuthUser
            {
                Id = dbUser.Id,
                Username = dbUser.Username,
                IsServerOwner = dbUser.IsServerOwner,
                AgeRestriction = dbUser.AgeRestriction?.Age,
                RestrictOnUnset = dbUser.AgeRestriction?.RestrictOnUnset ?? true,
                ExcludedLibraryIds = [.. dbUser.ExcludedLibraries.Select(e => e.LibraryId)]
            }
            : new AuthUser
            {
                Id = apiKey,
                Username = apiKey,
                IsServerOwner = false
            };

        return true;
    }

    [GeneratedRegex(@"^/opds/([^/]+)/v1\.2")]
    private static partial Regex MyRegex();
}

public static class OpdsAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseOpdsAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<OpdsAuthMiddleware>();
}
