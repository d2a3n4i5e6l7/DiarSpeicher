using Microsoft.Extensions.Configuration;

namespace DiarSpeicher.Client.Api;

public sealed record GatewayOptions(string UrlAccess, string AuthBase)
{
    public static GatewayOptions From(IConfiguration configuration) =>
        new(TrimSlash(configuration["Gateway:UrlAccess"]), TrimSlash(configuration["Gateway:AuthBase"]));

    private static string TrimSlash(string? value) => (value ?? string.Empty).TrimEnd('/');
}
