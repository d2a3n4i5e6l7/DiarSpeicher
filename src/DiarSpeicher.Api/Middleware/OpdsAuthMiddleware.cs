using System.Text.RegularExpressions;
using DiarSpeicher.Core.Domain.Models;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Middleware;

public partial class OpdsAuthMiddleware
{
    private const string AuthUserKey = "AuthUser";

    private static readonly string[] ProtectedPrefixes =
    [
        "/opds", "/api/v1", "/api/v2", "/koreader", "/kobo", "/graphql"
    ];

    private readonly RequestDelegate _next;
    private readonly GatewayOptions _gateway;

    public OpdsAuthMiddleware(RequestDelegate next, IOptions<GatewayOptions> gatewayOptions)
    {
        _next = next;
        _gateway = gatewayOptions.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!ProtectedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var apiKey = ExtractApiKey(path);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            context.Items["OpdsApiKey"] = apiKey;
        }

        if (context.Items.ContainsKey(AuthUserKey))
        {
            await _next(context);
            return;
        }

        // On a route the gateway serves publicly there is no identity to resolve: it answers
        // its own validation with 200 and no user headers. Rejecting here would take the route
        // down, so the request continues with an identity that carries no permissions.
        if (_gateway.IsPublicPath(path))
        {
            context.Items[AuthUserKey] = AuthUser.CreateAnonymous();
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new { error = "Identity not resolved by the gateway" },
            context.RequestAborted);
    }

    /// <summary>
    /// Reads the API key segment out of the path, for the sole purpose of putting it back into
    /// the links of the feeds so an e-reader keeps browsing under the same URL.
    /// <para>
    /// It is deliberately not read from the <c>X-Auth-Key</c> header nor from an
    /// <c>api_key</c> query parameter: those are credential channels, the gateway resolves them
    /// against its own store and blanks the header before proxying. The backend neither sees
    /// nor validates the key.
    /// </para>
    /// </summary>
    private static string? ExtractApiKey(string path)
    {
        var match = ApiKeyPathRegex.Match(path);
        if (!match.Success)
        {
            return null;
        }

        for (var i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success)
            {
                return match.Groups[i].Value;
            }
        }

        return null;
    }

    private static readonly Regex ApiKeyPathRegex = MyRegex();

    [GeneratedRegex(@"^/(?:opds/([^/]+)/(?:v1\.2|v2\.0)|koreader/([^/]+)|kobo/([^/]+))")]
    private static partial Regex MyRegex();
}

public static class OpdsAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseOpdsAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<OpdsAuthMiddleware>();
}
