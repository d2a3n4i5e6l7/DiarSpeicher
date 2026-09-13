using System.Text.Json;
using System.Text.RegularExpressions;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Api.Services;

public class HttpEpubProfileProvider : IEpubProfileProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HttpEpubProfileProvider> _logger;

    public HttpEpubProfileProvider(
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory,
        ILogger<HttpEpubProfileProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _scopeFactory = scopeFactory;
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
                "[EPUB Engine] Perfil personalizado '{Profile}' ({W}x{H}, AutoHeight={AH}) aplicado para UA: '{UA}'",
                customProfile.Name, customProfile.Width, customProfile.Height, customProfile.AutoHeight, userAgent);
            return customProfile;
        }

        var defaults = EpubDeviceProfile.GetDefaults();
        EpubDeviceProfile fallback;
        if (Regex.IsMatch(userAgent, "iPad|Tablet", RegexOptions.IgnoreCase))
        {
            fallback = defaults.FirstOrDefault(p => p.Id == "p_tablet") ?? defaults[0];
        }
        else
        {
            fallback = defaults.FirstOrDefault(p => p.Id == "p_cdisplay") ?? defaults[0];
        }

        _logger.LogInformation(
            "[EPUB Engine] Perfil por defecto '{Profile}' ({W}x{H}, AutoHeight={AH}) aplicado para UA: '{UA}'",
            fallback.Name, fallback.Width, fallback.Height, fallback.AutoHeight, userAgent);
        return fallback;
    }

    private async Task<EpubDeviceProfile?> TryGetUserCustomProfileAsync(HttpContext? httpContext, CancellationToken ct)
    {
        if (httpContext == null) return null;

        try
        {
            if (httpContext.Items["AuthUser"] is not AuthUser authUser) return null;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();
            var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == authUser.Id, ct);
            if (string.IsNullOrWhiteSpace(prefs?.EpubProfilesJson)) return null;

            var profiles = JsonSerializer.Deserialize<List<EpubDeviceProfile>>(prefs.EpubProfilesJson);
            if (profiles == null || profiles.Count == 0) return null;

            return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];
        }
        catch
        {
            return null;
        }
    }
}
