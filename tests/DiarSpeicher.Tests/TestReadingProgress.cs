using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

internal static class TestReadingProgress
{
    public static IReadingProgress For(DiarSpeicherDbContext db, EpubDeviceProfile? profile = null) =>
        new ReadingProgress(
            db,
            new EpubPageMapStore(db, NullLogger<EpubPageMapStore>.Instance),
            new FixedProfileProvider(profile ?? EpubDeviceProfile.GetDefaults()[0]),
            NullLogger<ReadingProgress>.Instance);

    private sealed class FixedProfileProvider : IEpubProfileProvider
    {
        private readonly EpubDeviceProfile _profile;

        public FixedProfileProvider(EpubDeviceProfile profile) => _profile = profile;

        public Task<EpubDeviceProfile> GetCurrentProfileAsync(CancellationToken ct = default) =>
            Task.FromResult(_profile);
    }
}
