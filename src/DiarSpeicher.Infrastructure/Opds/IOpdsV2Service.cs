using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;

namespace DiarSpeicher.Infrastructure.Opds;

public interface IOpdsV2Service
{
    Task<OpdsV2Feed> GetCatalogFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed> SearchFeedAsync(AuthUser user, string query, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed> GetLibrariesFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed> GetSeriesFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed?> GetLibrarySeriesFeedAsync(AuthUser user, string libraryId, int page, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed?> GetSeriesBooksFeedAsync(AuthUser user, string seriesId, int page, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed> GetBooksFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Feed> GetKeepReadingFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Publication?> GetPublicationAsync(AuthUser user, string bookId, string? apiKey, CancellationToken ct = default);
    Task<OpdsV2Progression?> GetProgressionAsync(AuthUser user, string bookId, CancellationToken ct = default);
    Task<bool> UpdateProgressionAsync(AuthUser user, string bookId, OpdsV2Progression progression, CancellationToken ct = default);
    OpdsV2AuthenticationDoc GetAuthenticationDoc(string? apiKey);
}
