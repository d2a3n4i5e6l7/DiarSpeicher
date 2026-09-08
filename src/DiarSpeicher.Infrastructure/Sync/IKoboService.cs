using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;

namespace DiarSpeicher.Infrastructure.Sync;

public interface IKoboService
{
    Task<Dictionary<string, object>> GetInitializationAsync(string baseUrl, string apiKey, CancellationToken ct = default);
    Task<KoboSyncResponse> SyncLibraryAsync(AuthUser user, string baseUrl, string apiKey, string? clientSyncToken, int limit = 100, CancellationToken ct = default);
    Task<KoboBookMetadata?> GetBookMetadataAsync(AuthUser user, string baseUrl, string apiKey, string bookId, CancellationToken ct = default);
    Task<(string Path, string ContentType)?> GetBookFileAsync(AuthUser user, string bookId, CancellationToken ct = default);
}
