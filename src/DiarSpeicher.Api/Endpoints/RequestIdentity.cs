using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Api.Endpoints;

internal static class RequestIdentity
{
    private const string AuthUserKey = "AuthUser";
    private const string ApiKeyItem = "OpdsApiKey";
    private const string ApiKeyRoute = "apiKey";

    public static AuthUser GetAuthUser(HttpContext context)
    {
        if (context.Items.TryGetValue(AuthUserKey, out var obj) && obj is AuthUser authUser)
        {
            return authUser;
        }

        return new AuthUser
        {
            Id = "anonymous",
            Username = "anonymous",
            IsServerOwner = false
        };
    }

    public static string? GetApiKey(HttpContext context)
    {
        if (context.Items.TryGetValue(ApiKeyItem, out var obj) && obj is string apiKey)
        {
            return apiKey;
        }

        if (context.Request.RouteValues.TryGetValue(ApiKeyRoute, out var routeValue) && routeValue != null)
        {
            return routeValue.ToString();
        }

        return null;
    }
}
