using DiarSpeicher.Core.Gateway;

namespace DiarSpeicher.Api.Middleware;

/// <summary>
/// Takes the prefix from the request. ASP.NET fills <c>Request.PathBase</c> on its own once
/// <c>UsePathBase</c> is in the pipeline, so this reflects whatever prefix the request actually
/// arrived under rather than a second copy of the configuration.
/// </summary>
public class HttpContextLinkPrefixProvider : ILinkPrefixProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextLinkPrefixProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Empty outside a request —a background service generating links, say— which yields
    /// root-relative links, the same behaviour as before the prefix existed.
    /// </summary>
    public string Prefix
    {
        get
        {
            var pathBase = _httpContextAccessor.HttpContext?.Request.PathBase.Value;

            return string.IsNullOrWhiteSpace(pathBase)
                ? string.Empty
                : "/" + pathBase.Trim('/');
        }
    }
}
