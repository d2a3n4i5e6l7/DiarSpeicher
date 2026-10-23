using Dapper;

namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public async Task<DiarSpeicherSystemStatusDto> GetSystemStatusAsync(CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT COUNT(*) FROM ""Users"";";
        var userCount = await conn.ExecuteScalarAsync<int>(sql);
        return new DiarSpeicherSystemStatusDto
        {
            Status = "OK",
            Semver = "0.1.0",
            IsClaimed = userCount > 0
        };
    }
}
