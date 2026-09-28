using DiarSpeicher.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace DiarSpeicher.Tests;

public sealed class TestSqliteDatabase : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;

    public ISqliteConnectionFactory Factory { get; }
    public SqliteConnection Connection => _keepAliveConnection;
    public string ConnectionString => _keepAliveConnection.ConnectionString;

    public TestSqliteDatabase()
    {
        var dbName = $"mem_{Guid.NewGuid():N}";
        var connStr = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        _keepAliveConnection = new SqliteConnection(connStr);
        _keepAliveConnection.Open();
        _keepAliveConnection.InitializeDatabase();

        Factory = new SqliteConnectionFactory(connStr);
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
        GC.SuppressFinalize(this);
    }
}
