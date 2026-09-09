namespace DiarSpeicher.Api.Middleware;

/// <summary>
/// The contract with the gateway that fronts this backend. Bound from the "Gateway"
/// configuration section.
/// </summary>
public class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// Public prefix the gateway mounts this service under, without slashes. Empty means
    /// served from the root.
    /// </summary>
    public string? PathBase { get; set; }

    /// <summary>
    /// Routes the gateway serves without an identity. On these the gateway answers its
    /// internal validation with 200 and no user headers, so <c>X-Auth-Sub</c> arrives empty
    /// and the request must be served anonymously rather than rejected.
    /// <para>
    /// Patterns match segment by segment, case-insensitively: <c>*</c> stands for exactly one
    /// segment —which is how the API key segment is skipped— and <c>**</c> for the rest of the
    /// path. Left empty, <see cref="DefaultPublicPaths"/> applies.
    /// </para>
    /// </summary>
    public List<string> PublicPaths { get; set; } = [];

    /// <summary>
    /// The OPDS 2.0 authentication document only: a client that does not yet have credentials
    /// has to be able to read it, which is the whole point of the document.
    /// </summary>
    public static readonly string[] DefaultPublicPaths =
    [
        "/opds/v2.0/auth",
        "/opds/*/v2.0/auth"
    ];

    public IReadOnlyList<string> ResolvePublicPaths() =>
        PublicPaths.Count > 0 ? PublicPaths : DefaultPublicPaths;

    /// <summary>
    /// True when the path is declared public. Expects the path as ASP.NET exposes it in
    /// <c>Request.Path</c>, which already has the PathBase stripped off.
    /// </summary>
    public bool IsPublicPath(string path)
    {
        foreach (var pattern in ResolvePublicPaths())
        {
            if (MatchesPattern(pattern, path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPattern(string pattern, string path)
    {
        var patternSegments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < patternSegments.Length; i++)
        {
            // "**" swallows whatever is left, the empty remainder included.
            if (patternSegments[i] == "**")
            {
                return true;
            }

            if (i >= pathSegments.Length)
            {
                return false;
            }

            if (patternSegments[i] == "*")
            {
                continue;
            }

            if (!patternSegments[i].Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Without a trailing "**" the match has to be exact, so a public /a/b does not open
        // everything under /a/b/.
        return patternSegments.Length == pathSegments.Length;
    }
}
