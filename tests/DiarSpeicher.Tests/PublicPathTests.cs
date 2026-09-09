using DiarSpeicher.Api.Middleware;
using DiarSpeicher.Core.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Tests;

/// <summary>
/// On a route the gateway serves publicly there is no identity to resolve: it answers its own
/// validation with 200 and no user headers. The backend has to serve those requests anonymously
/// instead of rejecting them.
/// </summary>
public class PublicPathTests
{
    private static async Task<(HttpContext Context, bool NextCalled)> InvokeAsync(
        string path,
        GatewayOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        var nextCalled = false;
        var middleware = new OpdsAuthMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(options ?? new GatewayOptions()));

        await middleware.InvokeAsync(context);

        return (context, nextCalled);
    }

    private static AuthUser? Resolved(HttpContext context) =>
        context.Items.TryGetValue("AuthUser", out var value) ? value as AuthUser : null;

    [Fact]
    public async Task ProtectedRouteWithoutIdentityIsRejected()
    {
        var (context, nextCalled) = await InvokeAsync("/opds/v1.2/catalog");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnprotectedRouteIsLetThroughWithoutIdentity()
    {
        var (context, nextCalled) = await InvokeAsync("/health");

        Assert.True(nextCalled);
        Assert.Null(Resolved(context));
    }

    /// <summary>
    /// A client with no credentials yet has to be able to read the OPDS 2.0 authentication
    /// document, which is the whole point of the document, so it is public by default.
    /// </summary>
    [Fact]
    public async Task AuthenticationDocumentIsPublicByDefault()
    {
        var (context, nextCalled) = await InvokeAsync("/opds/v2.0/auth");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.NotNull(Resolved(context));
        Assert.True(Resolved(context)!.IsAnonymous);
    }

    /// <summary>"*" skips exactly one segment, which is how the API key segment is matched.</summary>
    [Fact]
    public async Task AuthenticationDocumentIsPublicUnderAnApiKey()
    {
        var (_, nextCalled) = await InvokeAsync("/opds/some-key/v2.0/auth");

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AnonymousIdentityCarriesNoPermissions()
    {
        var (context, _) = await InvokeAsync("/opds/v2.0/auth");

        var user = Resolved(context)!;
        Assert.True(user.IsAnonymous);
        Assert.False(user.IsServerOwner);
        Assert.False(user.HasRole("admin"));
        Assert.True(user.RestrictOnUnset);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public async Task DeclaredPublicPathIsServedAnonymously()
    {
        var options = new GatewayOptions
        {
            PublicPaths = ["/opds/*/v1.2/books/*/thumbnail"]
        };

        var (context, nextCalled) = await InvokeAsync("/opds/k/v1.2/books/b1/thumbnail", options);

        Assert.True(nextCalled);
        Assert.True(Resolved(context)!.IsAnonymous);
    }

    /// <summary>
    /// Declaring a path public must not open everything beneath it: without a trailing "**" the
    /// match is exact.
    /// </summary>
    [Fact]
    public async Task PublicPathDoesNotOpenItsChildren()
    {
        var options = new GatewayOptions { PublicPaths = ["/opds/v1.2/catalog"] };

        var (context, nextCalled) = await InvokeAsync("/opds/v1.2/catalog/secret", options);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task DoubleStarOpensTheRemainderOfThePath()
    {
        var options = new GatewayOptions { PublicPaths = ["/opds/v1.2/public/**"] };

        var (_, nextCalled) = await InvokeAsync("/opds/v1.2/public/a/b/c", options);

        Assert.True(nextCalled);
    }

    /// <summary>
    /// Declaring any public path replaces the defaults, so the authentication document has to
    /// be declared again when the list is overridden.
    /// </summary>
    [Fact]
    public async Task DeclaringPublicPathsReplacesTheDefaults()
    {
        var options = new GatewayOptions { PublicPaths = ["/opds/v1.2/catalog"] };

        var (_, nextCalled) = await InvokeAsync("/opds/v2.0/auth", options);

        Assert.False(nextCalled);
    }

    /// <summary>
    /// The key is read from the path only to put it back into the links of the feeds. It is
    /// never read from the X-Auth-Key header nor from a query parameter: those are the
    /// gateway's business.
    /// </summary>
    [Fact]
    public async Task ApiKeyComesFromThePathOnly()
    {
        var (context, _) = await InvokeAsync("/opds/some-key/v2.0/auth");

        Assert.Equal("some-key", context.Items["OpdsApiKey"]);
    }

    [Fact]
    public async Task ApiKeyIsNotTakenFromTheHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/opds/v2.0/auth";
        context.Request.Headers["X-Auth-Key"] = "from-the-client";
        context.Request.QueryString = new QueryString("?api_key=also-from-the-client");

        var middleware = new OpdsAuthMiddleware(_ => Task.CompletedTask, Options.Create(new GatewayOptions()));
        await middleware.InvokeAsync(context);

        Assert.False(context.Items.ContainsKey("OpdsApiKey"));
    }
}
