using System.Security.Claims;
using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Api.Middleware;

public class GatewayIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public GatewayIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sub = context.Request.Headers["X-Auth-Sub"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(sub))
        {
            var roleHeader = context.Request.Headers["X-Auth-Role"].FirstOrDefault() ?? "";
            var isOwnerHeader = context.Request.Headers["X-Auth-Server-Owner"].FirstOrDefault() ?? "";
            var ageHeader = context.Request.Headers["X-Auth-Age-Restriction"].FirstOrDefault();

            var authUser = new AuthUser
            {
                Id = sub,
                Username = sub,
                IsServerOwner = bool.TryParse(isOwnerHeader, out var isOwner) && isOwner
            };

            foreach (var role in roleHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                authUser.Roles.Add(role);
            }

            if (int.TryParse(ageHeader, out var age))
            {
                authUser.AgeRestriction = age;
            }

            context.Items["AuthUser"] = authUser;

            // También mapeamos a ClaimsPrincipal para compatibilidad con Authorize si se usa
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, sub),
                new(ClaimTypes.Name, sub)
            };

            if (authUser.IsServerOwner)
            {
                claims.Add(new Claim("IsServerOwner", "true"));
            }

            foreach (var role in authUser.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "GatewayTrusted");
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }
}

public static class GatewayIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayIdentity(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayIdentityMiddleware>();
}
