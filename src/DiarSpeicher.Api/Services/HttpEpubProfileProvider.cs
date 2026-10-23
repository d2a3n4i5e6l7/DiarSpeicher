using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DiarSpeicher.Api.Serialization;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;

namespace DiarSpeicher.Api.Services;

public class HttpEpubProfileProvider : IEpubProfileProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<HttpEpubProfileProvider> _logger;

    public HttpEpubProfileProvider(
        IHttpContextAccessor httpContextAccessor,
        ISqliteConnectionFactory connectionFactory,
        ILogger<HttpEpubProfileProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<EpubDeviceProfile> GetCurrentProfileAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString() ?? "";

        var customProfile = await TryGetUserCustomProfileAsync(httpContext, ct);
        if (customProfile != null)
        {
            _logger.LogInformation(
                "[EPUB Engine] Perfil personalizado '{Profile}' ({W}x{H}) aplicado para UA: '{UA}'",
                customProfile.Name, customProfile.Width, customProfile.Height, userAgent);
            return customProfile;
        }

        var defaults = EpubDeviceProfile.GetDefaults();
        EpubDeviceProfile fallback;
        if (Regex.IsMatch(userAgent, "iPad|Tablet", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
        {
            fallback = defaults.FirstOrDefault(p => p.Id == "p_tablet") ?? defaults[0];
        }
        else
        {
            fallback = defaults.FirstOrDefault(p => p.Id == "p_cdisplay") ?? defaults[0];
        }

        _logger.LogInformation(
            "[EPUB Engine] Perfil por defecto '{Profile}' ({W}x{H}) aplicado para UA: '{UA}'",
            fallback.Name, fallback.Width, fallback.Height, userAgent);
        return fallback;
    }

    private async Task<EpubDeviceProfile?> TryGetUserCustomProfileAsync(HttpContext? httpContext, CancellationToken ct)
    {
        if (httpContext == null) return null;

        try
        {
            if (httpContext.Items["AuthUser"] is not AuthUser authUser) return null;

            await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
            const string sql = """SELECT "EpubProfilesJson" FROM "UserPreferences" WHERE "UserId" = @UserId LIMIT 1;""";
            var json = await conn.QueryFirstOrDefaultAsync<string>(
                sql, new { UserId = authUser.Id });

            if (string.IsNullOrWhiteSpace(json)) return null;

            var profiles = JsonSerializer.Deserialize(json, PersistedJsonContext.Default.ListEpubDeviceProfile);
            if (profiles == null || profiles.Count == 0) return null;

            return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
        }
        catch
        {
            return null;
        }
    }
}
