using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Core.Filesystem;

public interface IEpubProfileProvider
{
    Task<EpubDeviceProfile> GetCurrentProfileAsync(CancellationToken ct = default);
}
