namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public async Task<DiarSpeicherSystemStatusDto> GetSystemStatusAsync(CancellationToken ct = default)
    {
        var userCount = await _db.Users.CountAsync(ct);
        return new DiarSpeicherSystemStatusDto
        {
            Status = "OK",
            Semver = "0.1.0",
            IsClaimed = userCount > 0
        };
    }
}
