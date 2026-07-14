using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

/// <summary>
/// A reusable PostgreSQL Testcontainers fixture for integration tests.
/// Each test class using this fixture gets its own container and connection string.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;

    public PostgreSqlFixture()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
    }

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
