using System.Text.Json;
using DiarSpeicher.Api.Serialization;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Dapper;

namespace DiarSpeicher.Api.Endpoints;

public static partial class DiarSpeicherEndpoints
{
    private static void MapEpubRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/epub/{id}/toc", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var toc = await service.GetEpubTocAsync(user, id, ct);
            return toc == null ? Results.NotFound() : Results.Ok(toc);
        });

        group.MapGet("/epub/{id}/resource/{*resourcePath}", async (
            string id,
            string resourcePath,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var res = await service.GetEpubResourceAsync(user, id, resourcePath, ct);
            if (res == null) return Results.NotFound();

            httpContext.Response.Headers.CacheControl = httpContext.Request.Query.ContainsKey("v")
                ? CacheControlPrivateImmutable
                : CacheControlPublicOneDay;
            return Results.File(res.Value.Data, res.Value.ContentType);
        });

        group.MapGet("/epub/{id}/manifest", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var manifest = await service.GetEpubManifestAsync(user, id, ct);
            return manifest == null ? Results.NotFound() : Results.Ok(manifest);
        });

        group.MapGet("/epub/{id}/chapter/{index:int}", async (
            string id,
            int index,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var chapter = await service.GetEpubChapterAsync(user, id, index, SiblingResourceBase(httpContext.Request), ct);
            return chapter == null ? Results.NotFound() : Results.Ok(chapter);
        });
    }

    private static string SiblingResourceBase(HttpRequest request)
    {
        var chapterPath = $"{request.PathBase}{request.Path}";
        return chapterPath[..chapterPath.LastIndexOf("/chapter/", StringComparison.Ordinal)] + "/resource/";
    }

    private static void MapUserProfileRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/users/{id}/epub-profiles", HandleGetEpubProfiles);
        group.MapPut("/users/{id}/epub-profiles", HandlePutEpubProfiles);
    }

    private static async Task<IResult> HandleGetEpubProfiles(
        string id,
        HttpContext httpContext,
        [FromServices] ISqliteConnectionFactory connectionFactory,
        CancellationToken ct)
    {
        if (httpContext.Items[AuthUserKey] is not AuthUser user) return Results.Unauthorized();

        var targetId = string.Equals(id, "me", StringComparison.OrdinalIgnoreCase) ? user.Id : id;
        if (user.Id != targetId && !user.IsServerOwner && !user.HasRole("admin"))
        {
            return Results.Forbid();
        }

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        const string sql = """SELECT "EpubProfilesJson" FROM "UserPreferences" WHERE "UserId" = @UserId LIMIT 1;""";
        var json = await conn.QueryFirstOrDefaultAsync<string>(
            sql, new { UserId = targetId });

        if (json == null)
        {
            return Results.Ok(EpubDeviceProfile.GetDefaults());
        }

        try
        {
            var profiles = JsonSerializer.Deserialize(json, PersistedJsonContext.Default.ListEpubDeviceProfile);
            return Results.Ok(profiles ?? EpubDeviceProfile.GetDefaults());
        }
        catch
        {
            return Results.Ok(EpubDeviceProfile.GetDefaults());
        }
    }

    private static async Task<IResult> HandlePutEpubProfiles(
        string id,
        [FromBody] List<EpubDeviceProfile> profiles,
        HttpContext httpContext,
        [FromServices] ISqliteConnectionFactory connectionFactory,
        CancellationToken ct)
    {
        if (httpContext.Items[AuthUserKey] is not AuthUser user) return Results.Unauthorized();

        var targetId = string.Equals(id, "me", StringComparison.OrdinalIgnoreCase) ? user.Id : id;
        if (user.Id != targetId && !user.IsServerOwner && !user.HasRole("admin"))
        {
            return Results.Forbid();
        }

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var affectedBooks = await CountAffectedBooksAsync(conn, targetId, profiles, ct);
        await SaveUserPreferencesAsync(conn, targetId, profiles, ct);

        return Results.Ok(new EpubProfilesUpdatedResponse(true, profiles.Count, affectedBooks));
    }

    private static async Task<int> CountAffectedBooksAsync(
        System.Data.Common.DbConnection conn,
        string targetId,
        List<EpubDeviceProfile> profiles,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var newProfile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
        if (newProfile == null) return 0;

        var profileKey = EpubRasterizer.BuildProfileKey(newProfile);
        const string sql = """
            SELECT COUNT(*)
            FROM "ReadingSessions"
            WHERE "UserId" = @UserId
              AND "Status" = 'Reading'
              AND "RenderedPage" IS NOT NULL
              AND ("RenderedProfileKey" IS NULL OR "RenderedProfileKey" != @ProfileKey);
            """;

        return await conn.ExecuteScalarAsync<int>(
            sql, new { UserId = targetId, ProfileKey = profileKey });
    }

    /// <summary>
    /// The JSON is declared as string rather than var: Dapper.AOT cannot see the System.Text.Json
    /// generated context, so with var the argument type is unresolved and the call silently falls
    /// back to reflection-based Dapper, which fails under Native AOT.
    /// </summary>
    private static async Task SaveUserPreferencesAsync(
        System.Data.Common.DbConnection conn,
        string targetId,
        List<EpubDeviceProfile> profiles,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string json = JsonSerializer.Serialize(profiles, PersistedJsonContext.Default.ListEpubDeviceProfile);
        const string sql = """
            INSERT INTO "UserPreferences" ("UserId", "EpubProfilesJson")
            VALUES (@UserId, @Json)
            ON CONFLICT ("UserId") DO UPDATE SET "EpubProfilesJson" = excluded."EpubProfilesJson";
            """;

        await conn.ExecuteAsync(sql, new { UserId = targetId, Json = json });
    }
}
