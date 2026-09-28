using System.Security.Claims;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;

namespace DiarSpeicher.Api.Middleware;

public class GatewayIdentityMiddleware
{
    private const string AuthUserKey = "AuthUser";
    private readonly RequestDelegate _next;

    public GatewayIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISqliteConnectionFactory connectionFactory)
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
            connectionFactory,
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
        ISqliteConnectionFactory connectionFactory,
        string id,
        string username,
        CancellationToken ct)
    {
        await using var conn = await connectionFactory.CreateConnectionAsync(ct);

        const string selectByIdSql = """
            SELECT u."Id", u."Username", u."IsServerOwner", u."AvatarPath", u."AvatarMeta", u."AvatarUpdatedAt", u."CreatedAt",
                   a."Id", a."UserId", a."Age", a."RestrictOnUnset"
            FROM "Users" u
            LEFT JOIN "AgeRestrictions" a ON u."Id" = a."UserId"
            WHERE u."Id" = @Id
            LIMIT 1;
            """;

        var usersById = (await conn.QueryAsync<User, AgeRestriction?, User>(
            selectByIdSql,
            (user, age) =>
            {
                user.AgeRestriction = age;
                return user;
            },
            new { Id = id },
            splitOn: "Id")).ToList();

        var existing = usersById.FirstOrDefault();
        if (existing is not null)
        {
            if (existing.Username != username)
            {
                existing.Username = username;
                const string updateSql = """UPDATE "Users" SET "Username" = @Username WHERE "Id" = @Id;""";
                await conn.ExecuteAsync(new CommandDefinition(updateSql, new { Username = username, Id = id }, cancellationToken: ct));
            }

            await LoadExclusionsAsync(conn, existing, ct);
            return existing;
        }

        const string selectByNameSql = """
            SELECT u."Id", u."Username", u."IsServerOwner", u."AvatarPath", u."AvatarMeta", u."AvatarUpdatedAt", u."CreatedAt",
                   a."Id", a."UserId", a."Age", a."RestrictOnUnset"
            FROM "Users" u
            LEFT JOIN "AgeRestrictions" a ON u."Id" = a."UserId"
            WHERE u."Username" = @Username
            LIMIT 1;
            """;

        var usersByName = (await conn.QueryAsync<User, AgeRestriction?, User>(
            selectByNameSql,
            (user, age) =>
            {
                user.AgeRestriction = age;
                return user;
            },
            new { Username = username },
            splitOn: "Id")).ToList();

        var existingByUsername = usersByName.FirstOrDefault();
        if (existingByUsername is not null)
        {
            var oldId = existingByUsername.Id;
            await using var tx = await conn.BeginTransactionAsync(ct);
            const string remapSql = """
                UPDATE "UserPreferences" SET "UserId" = @NewId WHERE "UserId" = @OldId;
                UPDATE "AgeRestrictions" SET "UserId" = @NewId WHERE "UserId" = @OldId;
                UPDATE "LibraryExclusions" SET "UserId" = @NewId WHERE "UserId" = @OldId;
                UPDATE "ReadingSessions" SET "UserId" = @NewId WHERE "UserId" = @OldId;
                UPDATE "Users" SET "Id" = @NewId WHERE "Id" = @OldId;
                """;

            await conn.ExecuteAsync(new CommandDefinition(remapSql, new { NewId = id, OldId = oldId }, transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct);

            existingByUsername.Id = id;
            await LoadExclusionsAsync(conn, existingByUsername, ct);
            return existingByUsername;
        }

        const string countSql = """SELECT COUNT(*) FROM "Users";""";
        var totalUsers = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, cancellationToken: ct));
        var isFirstUser = totalUsers == 0;

        var now = DateTimeOffset.UtcNow;
        var created = new User
        {
            Id = id,
            Username = username,
            IsServerOwner = isFirstUser,
            CreatedAt = now
        };

        const string insertSql = """
            INSERT INTO "Users" ("Id", "Username", "IsServerOwner", "CreatedAt")
            VALUES (@Id, @Username, @IsServerOwner, @Now);
            """;

        await conn.ExecuteAsync(new CommandDefinition(insertSql, new { Id = id, Username = username, IsServerOwner = isFirstUser, Now = now }, cancellationToken: ct));
        return created;
    }

    private static async Task LoadExclusionsAsync(System.Data.Common.DbConnection conn, User user, CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "LibraryId", "UserId"
            FROM "LibraryExclusions"
            WHERE "UserId" = @UserId;
            """;

        var exclusions = await conn.QueryAsync<LibraryExclusion>(
            new CommandDefinition(sql, new { UserId = user.Id }, cancellationToken: ct));

        user.ExcludedLibraries = exclusions.ToList();
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
