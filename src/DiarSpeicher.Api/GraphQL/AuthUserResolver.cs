using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Api.GraphQL;

/// <summary>
/// Resolves the caller from the AuthUser the authentication middleware placed on the
/// HttpContext, which is the single source of truth for both REST and GraphQL. Requests
/// without one are rejected here, so no resolver ever runs unauthenticated.
/// </summary>
public class AuthUserResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthUserResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public AuthUser? Current =>
        _httpContextAccessor.HttpContext?.Items[GraphQLConstants.AuthUserKey] as AuthUser;

    public AuthUser Require() =>
        Current ?? throw new GraphQLException("Authentication is required.");
}
