namespace DiarSpeicher.Infrastructure.Reading;

public interface IEpubPageMapStore
{
    Task<int> GetTotalPagesAsync(Media book, EpubDeviceProfile profile, CancellationToken ct = default);
}
