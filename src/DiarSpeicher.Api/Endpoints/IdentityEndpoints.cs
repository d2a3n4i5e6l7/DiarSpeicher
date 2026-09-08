using DiarSpeicher.Core.Domain.Identity;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static class IdentityEndpoints
{
    private const string AuthUserKey = "AuthUser";

    public static RouteGroupBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2");

        group.MapPost("/claim", async (
            [FromBody] ClaimServerRequest request,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            var result = await identity.ClaimAsync(request, ct);

            return result.Succeeded
                ? Results.Created($"/api/v2/users/{result.Value!.Id}", result.Value)
                : ToProblem(result.Error, result.Message);
        });

        MapUserRoutes(group);
        MapApiKeyRoutes(group);

        return group;
    }

    private static void MapUserRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/users", async (
            HttpContext httpContext,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireServerOwner(httpContext) is { } denial) return denial;

            return Results.Ok(await identity.GetUsersAsync(ct));
        });

        group.MapPost("/users", async (
            HttpContext httpContext,
            [FromBody] CreateUserRequest request,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireServerOwner(httpContext) is { } denial) return denial;

            var result = await identity.CreateUserAsync(request, ct);

            return result.Succeeded
                ? Results.Created($"/api/v2/users/{result.Value!.Id}", result.Value)
                : ToProblem(result.Error, result.Message);
        });

        group.MapPut("/users/{id}", async (
            string id,
            HttpContext httpContext,
            [FromBody] UpdateUserRequest request,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireServerOwner(httpContext) is { } denial) return denial;

            var result = await identity.UpdateUserAsync(id, request, ct);

            return result.Succeeded
                ? Results.Ok(result.Value)
                : ToProblem(result.Error, result.Message);
        });

        group.MapDelete("/users/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireServerOwner(httpContext) is { } denial) return denial;

            var result = await identity.DeleteUserAsync(id, ct);

            return result.Succeeded
                ? Results.NoContent()
                : ToProblem(result.Error, result.Message);
        });
    }

    /// <summary>
    /// A user manages their own keys; the server owner may manage anyone's. The owner check
    /// runs only when the target differs from the caller, so a non-owner keeps access to
    /// their own keys.
    /// </summary>
    private static void MapApiKeyRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/users/{id}/api-keys", async (
            string id,
            HttpContext httpContext,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireSelfOrServerOwner(httpContext, id) is { } denial) return denial;

            return Results.Ok(await identity.GetApiKeysAsync(id, ct));
        });

        group.MapPost("/users/{id}/api-keys", async (
            string id,
            HttpContext httpContext,
            [FromBody] CreateApiKeyRequest request,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireSelfOrServerOwner(httpContext, id) is { } denial) return denial;

            var result = await identity.CreateApiKeyAsync(id, request, ct);

            return result.Succeeded
                ? Results.Created($"/api/v2/users/{id}/api-keys/{result.Value!.Id}", result.Value)
                : ToProblem(result.Error, result.Message);
        });

        group.MapDelete("/users/{id}/api-keys/{keyId}", async (
            string id,
            string keyId,
            HttpContext httpContext,
            [FromServices] IIdentityService identity,
            CancellationToken ct) =>
        {
            if (RequireSelfOrServerOwner(httpContext, id) is { } denial) return denial;

            var result = await identity.RevokeApiKeyAsync(id, keyId, ct);

            return result.Succeeded
                ? Results.NoContent()
                : ToProblem(result.Error, result.Message);
        });
    }

    private static IResult? RequireServerOwner(HttpContext httpContext)
    {
        if (httpContext.Items[AuthUserKey] is not AuthUser user) return Results.Unauthorized();

        return user.IsServerOwner ? null : Results.Forbid();
    }

    private static IResult? RequireSelfOrServerOwner(HttpContext httpContext, string userId)
    {
        if (httpContext.Items[AuthUserKey] is not AuthUser user) return Results.Unauthorized();

        return user.IsServerOwner || string.Equals(user.Id, userId, StringComparison.Ordinal)
            ? null
            : Results.Forbid();
    }

    private static IResult ToProblem(IdentityErrorCode error, string? message) => error switch
    {
        IdentityErrorCode.AlreadyClaimed => Results.Conflict(new { error = message }),
        IdentityErrorCode.UsernameTaken => Results.Conflict(new { error = message }),
        IdentityErrorCode.UserNotFound => Results.NotFound(new { error = message }),
        _ => Results.BadRequest(new { error = message })
    };
}
