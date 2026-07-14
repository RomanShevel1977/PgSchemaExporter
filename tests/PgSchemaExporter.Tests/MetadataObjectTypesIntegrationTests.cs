using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class MetadataObjectTypesIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = "";
    private readonly string _tempRoot;

    public MetadataObjectTypesIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-metadata-" + Guid.NewGuid().ToString("n"));
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_LoadsSequences()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE SEQUENCE {SqlIdentifier.Qualified(schema, "my_seq")} START 10 INCREMENT 2;");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Sequences = true });

        Assert.Contains(model.Sequences, s => s.Name == "my_seq");
    }

    [Fact]
    public async Task LoadAsync_LoadsPublications()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        var pubName = "pub_" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE PUBLICATION {SqlIdentifier.Quote(pubName)} FOR TABLE {SqlIdentifier.Qualified(schema, "users")};");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Tables = true, Publications = true });

        Assert.Contains(model.Publications, p => p.Name == pubName);
    }

    [Fact]
    public async Task LoadAsync_LoadsPolicies()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ENABLE ROW LEVEL SECURITY;");
        await ExecuteNonQueryAsync($"CREATE POLICY {SqlIdentifier.Quote("select_all")} ON {SqlIdentifier.Qualified(schema, "users")} FOR SELECT USING (true);");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Tables = true, Policies = true });

        Assert.Contains(model.Policies, p => p.Name == "select_all");
    }

    [Fact]
    public async Task LoadAsync_LoadsComments()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        await ExecuteNonQueryAsync($"COMMENT ON TABLE {SqlIdentifier.Qualified(schema, "users")} IS 'users table';");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Tables = true, Comments = true });

        Assert.Contains(model.Comments, c => c.ObjectName == "users" && c.ObjectType == "TABLE");
    }

    [Fact]
    public async Task LoadAsync_LoadsGrants()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        await ExecuteNonQueryAsync($"GRANT SELECT ON {SqlIdentifier.Qualified(schema, "users")} TO PUBLIC;");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Tables = true, Grants = true });

        Assert.Contains(model.Grants, g => g.ObjectName == "users" && g.ObjectType == "TABLE");
    }

    [Fact]
    public async Task LoadAsync_LoadsEventTriggers()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE FUNCTION {SqlIdentifier.Qualified(schema, "evt_log")}() RETURNS event_trigger LANGUAGE plpgsql AS $$ BEGIN END; $$;");
        var triggerName = "evt_" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE EVENT TRIGGER {SqlIdentifier.Quote(triggerName)} ON ddl_command_start EXECUTE FUNCTION {SqlIdentifier.Qualified(schema, "evt_log")}();");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, EventTriggers = true });

        Assert.Contains(model.EventTriggers, e => e.Name == triggerName);
    }

    [Fact]
    public async Task LoadAsync_LoadsRules()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users_log")} ({SqlIdentifier.Quote("id")} integer);");
        await ExecuteNonQueryAsync($"CREATE RULE {SqlIdentifier.Quote("log_insert")} AS ON INSERT TO {SqlIdentifier.Qualified(schema, "users")} DO ALSO INSERT INTO {SqlIdentifier.Qualified(schema, "users_log")} VALUES (NEW.id);");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Tables = true, Rules = true });

        Assert.Contains(model.Rules, r => r.Name == "log_insert");
    }

    [Fact]
    public async Task LoadAsync_LoadsAggregates()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE FUNCTION {SqlIdentifier.Qualified(schema, "my_sfunc")}(text, text) RETURNS text LANGUAGE sql AS $$ SELECT $1 || $2; $$;");
        await ExecuteNonQueryAsync($"CREATE AGGREGATE {SqlIdentifier.Qualified(schema, "my_agg")}(text) (SFUNC = {SqlIdentifier.Qualified(schema, "my_sfunc")}, STYPE = text);");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Aggregates = true });

        Assert.Contains(model.Aggregates, a => a.Name == "my_agg");
    }

    [Fact]
    public async Task LoadAsync_LoadsOperators()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE FUNCTION {SqlIdentifier.Qualified(schema, "add_ints")}(integer, integer) RETURNS integer LANGUAGE sql AS $$ SELECT $1 + $2; $$;");
        await ExecuteNonQueryAsync($"SET search_path = {SqlIdentifier.Quote(schema)}; CREATE OPERATOR ## (FUNCTION = {SqlIdentifier.Qualified(schema, "add_ints")}, LEFTARG = integer, RIGHTARG = integer);");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Operators = true });

        Assert.Contains(model.Operators, o => o.Name == "##");
    }

    [Fact]
    public async Task LoadAsync_LoadsCasts()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TYPE {SqlIdentifier.Qualified(schema, "my_type")} AS (a integer);");
        await ExecuteNonQueryAsync($"CREATE FUNCTION {SqlIdentifier.Qualified(schema, "my_type_to_text")}({SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote("my_type")}) RETURNS text LANGUAGE sql AS $$ SELECT 'my_type'; $$;");
        await ExecuteNonQueryAsync($"CREATE CAST ({SqlIdentifier.Qualified(schema, "my_type")} AS text) WITH FUNCTION {SqlIdentifier.Qualified(schema, "my_type_to_text")}({SqlIdentifier.Qualified(schema, "my_type")});");

        var model = await LoadAsync(schema, new IncludeOptions { Schemas = true, Types = true, Functions = true, Casts = true });

        Assert.Contains(model.Casts, c => c.SourceType.Contains("my_type") && c.TargetType == "text");
    }

    private async Task<string> CreateSchemaAsync()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE SCHEMA {SqlIdentifier.Quote(schema)};");
        return schema;
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<DatabaseModel> LoadAsync(string schema, IncludeOptions include)
    {
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n")),
            Schemas = [schema],
            Include = include
        };

        return await provider.LoadAsync(
            _connectionString,
            options,
            NullProgressReporter.Instance,
            NullLogger.Instance);
    }
}
