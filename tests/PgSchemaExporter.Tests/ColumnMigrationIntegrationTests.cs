using Npgsql;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Plan;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class ColumnMigrationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = string.Empty;
    private readonly string _tempRoot;

    public ColumnMigrationIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-columns-" + Guid.NewGuid().ToString("n"));
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
    public async Task ColumnRemove_ApplyUpRemovesAndDownRestores()
    {
        var schema = "public";
        var table = SqlIdentifier.Qualified(schema, "users");
        var (fromDir, toDir) = PrepareDirectories(schema,
            $"CREATE TABLE {table} (\"id\" integer, \"email\" character varying(255));",
            $"CREATE TABLE {table} (\"id\" integer);");

        await CreateTableAsync(table, fromDir);
        await ApplyUp(fromDir, toDir, schema);
        Assert.False(await ColumnExistsAsync(schema, "users", "email"));

        await ApplyDown(fromDir, toDir, schema);
        Assert.True(await ColumnExistsAsync(schema, "users", "email"));
    }

    [Fact]
    public async Task ColumnTypeChange_ApplyUpChangesType()
    {
        var schema = "public";
        var table = SqlIdentifier.Qualified(schema, "users");
        var (fromDir, toDir) = PrepareDirectories(schema,
            $"CREATE TABLE {table} (\"id\" integer, \"email\" character varying(255));",
            $"CREATE TABLE {table} (\"id\" integer, \"email\" text);");

        await CreateTableAsync(table, fromDir);
        await ApplyUp(fromDir, toDir, schema);
        Assert.Equal("text", await GetColumnTypeAsync(schema, "users", "email"));
    }

    [Fact]
    public async Task ColumnNullabilityChange_ApplyUpSetsNotNull()
    {
        var schema = "public";
        var table = SqlIdentifier.Qualified(schema, "users");
        var (fromDir, toDir) = PrepareDirectories(schema,
            $"CREATE TABLE {table} (\"id\" integer, \"email\" character varying(255));",
            $"CREATE TABLE {table} (\"id\" integer, \"email\" character varying(255) NOT NULL);");

        await CreateTableAsync(table, fromDir);
        await ApplyUp(fromDir, toDir, schema);
        Assert.True(await IsColumnNotNullAsync(schema, "users", "email"));
    }

    [Fact]
    public async Task ColumnDefaultChange_ApplyUpSetsDefault()
    {
        var schema = "public";
        var table = SqlIdentifier.Qualified(schema, "users");
        var (fromDir, toDir) = PrepareDirectories(schema,
            $"CREATE TABLE {table} (\"id\" integer, \"status\" character varying(50));",
            $"CREATE TABLE {table} (\"id\" integer, \"status\" character varying(50) DEFAULT 'active');");

        await CreateTableAsync(table, fromDir);
        await ApplyUp(fromDir, toDir, schema);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT column_default FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = 'users' AND column_name = 'status';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.Equal("'active'::character varying", result);
    }

    [Fact]
    public async Task ColumnCollationChange_ApplyUpChangesCollation()
    {
        var schema = "public";
        var table = SqlIdentifier.Qualified(schema, "users");
        var (fromDir, toDir) = PrepareDirectories(schema,
            $"CREATE TABLE {table} (\"id\" integer, \"name\" character varying(100));",
            $"CREATE TABLE {table} (\"id\" integer, \"name\" character varying(100) COLLATE \"C\");");

        await CreateTableAsync(table, fromDir);
        await ApplyUp(fromDir, toDir, schema);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT collation_name FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = 'users' AND column_name = 'name';";
        var result = await cmd.ExecuteScalarAsync();
        Assert.Equal("C", result);
    }

    private (string fromDir, string toDir) PrepareDirectories(string schema, string fromSql, string toSql)
    {
        var fromDir = Path.Combine(_tempRoot, schema, "from");
        var toDir = Path.Combine(_tempRoot, schema, "to");

        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        File.WriteAllText(Path.Combine(fromDir, "tables", $"{schema}.users.sql"), fromSql);
        File.WriteAllText(Path.Combine(toDir, "tables", $"{schema}.users.sql"), toSql);

        return (fromDir, toDir);
    }

    private async Task CreateTableAsync(string table, string fromDir)
    {
        var fromSql = File.ReadAllText(Path.Combine(fromDir, "tables", $"public.users.sql"));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS {table};";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = fromSql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ApplyUp(string fromDir, string toDir, string schema)
    {
        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir });

        var applier = new MigrationApplier();
        await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false
        });
    }

    private async Task ApplyDown(string fromDir, string toDir, string schema)
    {
        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir });

        var applier = new MigrationApplier();
        await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = true
        });
    }

    private async Task<bool> ColumnExistsAsync(string schema, string table, string column)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = @column;";
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        cmd.Parameters.AddWithValue("column", column);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task<string> GetColumnTypeAsync(string schema, string table, string column)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data_type FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = @column;";
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        cmd.Parameters.AddWithValue("column", column);
        return (await cmd.ExecuteScalarAsync() as string) ?? "";
    }

    private async Task<bool> IsColumnNotNullAsync(string schema, string table, string column)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT is_nullable FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = @column;";
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        cmd.Parameters.AddWithValue("column", column);
        var result = await cmd.ExecuteScalarAsync() as string;
        return result == "NO";
    }
}
