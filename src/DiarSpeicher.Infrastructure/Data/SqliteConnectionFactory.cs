using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace DiarSpeicher.Infrastructure.Data;

public interface ISqliteConnectionFactory
{
    string ConnectionString { get; }
    SqliteConnection CreateConnection();
    Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
    Task<SqliteConnection> CreateConnectionAsync(CancellationToken ct = default);
}

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("DefaultConnection")
                  ?? "Data Source=diarspeicher.db;Cache=Shared;Mode=ReadWriteCreate;";

        var builder = new SqliteConnectionStringBuilder(raw)
        {
            ForeignKeys = true,
            DefaultTimeout = 30
        };
        _connectionString = builder.ToString();
    }

    public SqliteConnectionFactory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            ForeignKeys = true,
            DefaultTimeout = 30
        };
        _connectionString = builder.ToString();
    }

    public string ConnectionString => _connectionString;

    public SqliteConnection CreateConnection() => new(_connectionString);

    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public Task<SqliteConnection> CreateConnectionAsync(CancellationToken ct = default) => CreateOpenConnectionAsync(ct);
}
