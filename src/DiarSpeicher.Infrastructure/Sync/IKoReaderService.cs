using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;

namespace DiarSpeicher.Infrastructure.Sync;

public interface IKoReaderService
{
    Task<KoReaderAuthResponse> CheckAuthorizedAsync(CancellationToken ct = default);
    Task<KoReaderProgressResponse> GetProgressAsync(AuthUser user, string koreaderHash, CancellationToken ct = default);
    Task<KoReaderPutProgressResponse> UpdateProgressAsync(AuthUser user, KoReaderProgressInput input, CancellationToken ct = default);
}
