using System.Text;
using System.Text.RegularExpressions;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Security;
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

    public async Task InvokeAsync(HttpContext context, DiarSpeicherDbContext db, IPasswordHasher passwordHasher)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/opds", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/api/v2", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/koreader", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/kobo", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/graphql", StringComparison.OrdinalIgnoreCase))
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

        // Claiming is what creates the first user, so it cannot require one. The endpoint
        // itself rejects a second claim with 409; blocking it here would report 401 instead.
        if (IsClaimRequest(context, path))
        {
            await _next(context);
            return;
        }

        if (await TryBasicAuthAsync(context, db, passwordHasher, ct))
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

    private static bool IsClaimRequest(HttpContext context, string path) =>
        HttpMethods.IsPost(context.Request.Method) &&
        path.Equals("/api/v2/claim", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractApiKey(HttpContext context, string path)
    {
        var match = ApiKeyPathRegex.Match(path);
        if (match.Success)
        {
            for (int i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                {
                    return match.Groups[i].Value;
                }
            }
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

    private static async Task<bool> TryBasicAuthAsync(HttpContext context, DiarSpeicherDbContext db, IPasswordHasher passwordHasher, CancellationToken ct)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string username;
        string password;
        try
        {
            var base64 = authHeader[6..].Trim();
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var separatorIndex = credentials.IndexOf(':');
            if (separatorIndex < 0)
            {
                return false;
            }

            username = credentials[..separatorIndex];
            password = credentials[(separatorIndex + 1)..];
        }
        catch (FormatException)
        {
            return false;
        }

        var dbUser = await LoadActiveUserAsync(db, u => u.Username == username || u.Id == username, ct);
        if (dbUser == null || !passwordHasher.Verify(password, dbUser.HashedPassword))
        {
            return false;
        }

        context.Items[AuthUserKey] = ToAuthUser(dbUser);
        return true;
    }

    private static async Task<bool> TryApiKeyAuthAsync(HttpContext context, DiarSpeicherDbContext db, string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        var keyHash = ApiKeyGenerator.HashKey(apiKey);
        var now = DateTimeOffset.UtcNow;

        var record = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct);

        if (record == null || (record.ExpiresAt != null && record.ExpiresAt <= now))
        {
            return false;
        }

        var dbUser = await LoadActiveUserAsync(db, u => u.Id == record.UserId, ct);
        if (dbUser == null)
        {
            return false;
        }

        context.Items[AuthUserKey] = ToAuthUser(dbUser);
        return true;
    }

    private static Task<User?> LoadActiveUserAsync(
        DiarSpeicherDbContext db,
        System.Linq.Expressions.Expression<Func<User, bool>> predicate,
        CancellationToken ct) =>
        db.Users
            .AsNoTracking()
            .Include(u => u.AgeRestriction)
            .Include(u => u.ExcludedLibraries)
            .Where(u => !u.IsLocked && u.DeletedAt == null)
            .FirstOrDefaultAsync(predicate, ct);

    private static AuthUser ToAuthUser(User dbUser) => new()
    {
        Id = dbUser.Id,
        Username = dbUser.Username,
        IsServerOwner = dbUser.IsServerOwner,
        AgeRestriction = dbUser.AgeRestriction?.Age,
        RestrictOnUnset = dbUser.AgeRestriction?.RestrictOnUnset ?? true,
        ExcludedLibraryIds = [.. dbUser.ExcludedLibraries.Select(e => e.LibraryId)]
    };

    [GeneratedRegex(@"^/(?:opds/([^/]+)/(?:v1\.2|v2\.0)|koreader/([^/]+)|kobo/([^/]+))")]
    private static partial Regex MyRegex();
}

public static class OpdsAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseOpdsAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<OpdsAuthMiddleware>();
}
