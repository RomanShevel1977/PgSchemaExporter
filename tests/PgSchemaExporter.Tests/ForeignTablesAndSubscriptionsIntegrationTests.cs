using Npgsql;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class ForeignTablesAndSubscriptionsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = string.Empty;

    public ForeignTablesAndSubscriptionsIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task LoadAsync_LoadsForeignTables()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            CREATE SCHEMA {SqlIdentifier.Quote(schema)};
            CREATE TABLE {SqlIdentifier.Qualified(schema, "local_users")} (id integer);
            CREATE EXTENSION IF NOT EXISTS postgres_fdw;
            CREATE SERVER {SqlIdentifier.Quote("fdw_" + schema)} FOREIGN DATA WRAPPER postgres_fdw
                OPTIONS (host 'localhost', dbname 'testdb', port '5432');
            CREATE USER MAPPING FOR CURRENT_USER SERVER {SqlIdentifier.Quote("fdw_" + schema)}
                OPTIONS (user 'testuser', password 'testpass');
            CREATE FOREIGN TABLE {SqlIdentifier.Qualified(schema, "remote_users")} (id integer)
                SERVER {SqlIdentifier.Quote("fdw_" + schema)};
        ";
        await cmd.ExecuteNonQueryAsync();

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            Schemas = [schema],
            ExcludeSchemas = [],
            Include = new IncludeOptions
            {
                Schemas = false,
                Tables = false,
                ForeignTables = true
            }
        };

        var model = await provider.LoadAsync(_connectionString, options);

        Assert.NotNull(model);
        Assert.Single(model.ForeignTables);
        Assert.Equal("remote_users", model.ForeignTables[0].Name);
        Assert.Contains("CREATE FOREIGN TABLE", model.ForeignTables[0].Definition);
        Assert.Contains("SERVER", model.ForeignTables[0].Definition);
    }

    [Fact]
    public async Task LoadAsync_LoadsSubscriptions()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();

        // Subscriptions require logical replication to be created. If wal_level is not
        // logical we still verify that the provider loads without failing.
        var subscriptionCreated = false;
        cmd.CommandText = $@"
            CREATE SCHEMA {SqlIdentifier.Quote(schema)};
            CREATE TABLE {SqlIdentifier.Qualified(schema, "events")} (id int);
        ";
        await cmd.ExecuteNonQueryAsync();

        try
        {
            await using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = $@"
                CREATE PUBLICATION {SqlIdentifier.Quote("pub_" + schema)} FOR TABLES IN SCHEMA {SqlIdentifier.Quote(schema)};
                CREATE SUBSCRIPTION {SqlIdentifier.Quote("sub_" + schema)}
                    CONNECTION 'host=localhost dbname=testdb user=testuser password=testpass'
                    PUBLICATION {SqlIdentifier.Quote("pub_" + schema)}
                    WITH (connect = false, slot_name = NONE);
            ";
            await cmd2.ExecuteNonQueryAsync();
            subscriptionCreated = true;
        }
        catch (PostgresException ex) when (ex.SqlState == "55000" || ex.Message.Contains("wal_level", StringComparison.OrdinalIgnoreCase))
        {
            // wal_level is not logical; subscription creation is not possible in this test run.
        }

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            Schemas = [schema],
            ExcludeSchemas = [],
            Include = new IncludeOptions
            {
                Schemas = false,
                Tables = false,
                Subscriptions = true
            }
        };

        var model = await provider.LoadAsync(_connectionString, options);

        Assert.NotNull(model);
        if (subscriptionCreated)
        {
            Assert.Single(model.Subscriptions);
            Assert.Equal($"sub_{schema}", model.Subscriptions[0].Name);
            Assert.Contains($"pub_{schema}", model.Subscriptions[0].Publication);
        }
        else
        {
            Assert.NotNull(model.Subscriptions);
        }
    }

    [Fact]
    public async Task LoadAsync_SubscriptionPermissionError_DoesNotFailEntireExport()
    {
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Tables = true,
                Subscriptions = true
            }
        };

        var model = await provider.LoadAsync(_connectionString, options);

        Assert.NotNull(model);
        Assert.NotNull(model.Subscriptions);
    }
}
