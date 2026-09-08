using System.Text.RegularExpressions;

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

    public async Task InvokeAsync(HttpContext context)
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

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new { error = "Identity not resolved by the gateway" },
            context.RequestAborted);
    }

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

    [GeneratedRegex(@"^/(?:opds/([^/]+)/(?:v1\.2|v2\.0)|koreader/([^/]+)|kobo/([^/]+))")]
    private static partial Regex MyRegex();
}

public static class OpdsAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseOpdsAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<OpdsAuthMiddleware>();
}
