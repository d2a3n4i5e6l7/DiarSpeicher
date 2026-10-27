using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Client.Api;

public sealed class EpubProfilesApi(HttpClient http, GatewayOptions gateway)
{
    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<List<EpubDeviceProfile>?> GetProfilesAsync(int userId) =>
        _client.GetAsync($"/api/v2/users/{userId}/epub-profiles", CatalogJsonContext.Default.ListEpubDeviceProfile);

    public Task<CountedUpdateResult?> SaveProfilesAsync(int userId, List<EpubDeviceProfile> profiles) =>
        _client.PutAsync($"/api/v2/users/{userId}/epub-profiles", profiles,
            CatalogJsonContext.Default.ListEpubDeviceProfile, CatalogJsonContext.Default.CountedUpdateResult);
}
