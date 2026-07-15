using Npgsql;
using PgSchemaExporter.Core;
using Testcontainers.PostgreSql;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Plan;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class PlanApplyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = "";
    private readonly string _tempRoot;

    public PlanApplyIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-plan-apply-" + Guid.NewGuid().ToString("n"));
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
    public async Task PlanWorkflow_GeneratesPlanWithChangesAndHazards()
    {
        // Arrange
        var (fromDir, toDir, _) = await CreateFromToWithTableRemovalAsync();

        var options = new MigrationOptions
        {
            FromDirectory = fromDir,
            ToDirectory = toDir,
            Name = "drop_table"
        };

        // Act
        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(options);

        // Assert
        Assert.True(plan.HasChanges);
        Assert.Contains(plan.Up, s => s.Sql.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Down, s => s.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));
        Assert.True(plan.HasDestructiveChanges);
        Assert.Contains(plan.Hazards, h => h.Category == "TableDrop");
    }

    [Fact]
    public async Task PlanWorkflow_SerializesAndDeserializes()
    {
        // Arrange
        var (fromDir, toDir, _) = await CreateFromToWithAddedColumnAsync();
        var options = new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir };
        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(options);

        var planFile = Path.Combine(_tempRoot, "plan.json");

        // Act
        await MigrationPlanFile.WriteAsync(planFile, plan);
        var roundTripped = await MigrationPlanFile.ReadAsync(planFile);

        // Assert
        Assert.Equal(plan.Up.Count, roundTripped.Up.Count);
        Assert.Equal(plan.Down.Count, roundTripped.Down.Count);
        Assert.Equal(plan.Hazards.Count, roundTripped.Hazards.Count);
    }

    [Fact]
    public async Task PlanWorkflow_RendersHumanReadable()
    {
        // Arrange
        var (fromDir, toDir, _) = await CreateFromToWithTableRemovalAsync();
        var options = new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir, Safe = true };
        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(options);

        // Act
        var rendered = MigrationPlanRenderer.RenderHuman(plan);

        // Assert
        Assert.Contains("Migration plan", rendered);
        Assert.Contains("Hazards", rendered);
        Assert.Contains("[HIGH] TableDrop", rendered);
    }

    [Fact]
    public async Task ApplyWorkflow_UpAndDown_ColumnChange()
    {
        // Arrange
        var (fromDir, toDir, schema) = await CreateFromToWithAddedColumnAsync();

        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir });

        var applier = new MigrationApplier();

        // Act - apply up
        var upResult = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false,
            DryRun = false
        });

        // Assert
        Assert.Equal(1, upResult.Executed);
        Assert.Equal(0, upResult.Skipped);
        Assert.True(await ColumnExistsAsync(schema, "users", "email"));

        // Act - apply down
        var downResult = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = true,
            DryRun = false
        });

        // Assert
        Assert.Equal(1, downResult.Executed);
        Assert.False(await ColumnExistsAsync(schema, "users", "email"));
    }

    [Fact]
    public async Task ApplyWorkflow_DryRun_DoesNotModifyDatabase()
    {
        // Arrange
        var (fromDir, toDir, schema) = await CreateFromToWithAddedColumnAsync();

        var plan = new MigrationPlanner().CreatePlan(new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir });

        // Act
        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false,
            DryRun = true
        });

        // Assert
        Assert.True(result.DryRun);
        Assert.Equal(0, result.Executed);
        Assert.False(await ColumnExistsAsync(schema, "users", "email"));
    }

    [Fact]
    public async Task ApplyWorkflow_SafeMode_SkipsDestructive()
    {
        // Arrange
        var (fromDir, toDir, schema) = await CreateFromToWithTableRemovalAsync();

        var options = new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir, Safe = true };
        var plan = new MigrationPlanner().CreatePlan(options);

        // Act
        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false,
            DryRun = false
        });

        // Assert
        Assert.Equal(0, result.Executed);
        Assert.Equal(1, result.Skipped);
        Assert.True(await TableExistsAsync(schema, "users"));
    }

    [Fact]
    public async Task ApplyWorkflow_WithTimeouts_SetsSessionGuards()
    {
        // Arrange
        var (fromDir, toDir, schema) = await CreateFromToWithAddedColumnAsync();

        var options = new MigrationOptions
        {
            FromDirectory = fromDir,
            ToDirectory = toDir,
            LockTimeout = "5s",
            StatementTimeout = "30s"
        };

        var plan = new MigrationPlanner().CreatePlan(options);

        // Act
        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false
        });

        // Assert
        Assert.Equal(1, result.Executed);
        Assert.True(await ColumnExistsAsync(schema, "users", "email"));
        Assert.Equal("5s", plan.Settings.LockTimeout);
        Assert.Equal("30s", plan.Settings.StatementTimeout);
    }

    [Fact]
    public async Task ApplyWorkflow_ConcurrentIndex_RunsOutsideTransaction()
    {
        // Arrange
        var (fromDir, toDir, schema) = await CreateFromToWithAddedIndexAsync();

        var options = new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir, OnlineDdl = true };
        var plan = new MigrationPlanner().CreatePlan(options);

        // Assert that the plan contains a concurrent statement outside of a transaction
        var createIndex = plan.Up.First(s => s.Sql.Contains("CREATE INDEX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("CONCURRENTLY", createIndex.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(createIndex.RunsOutsideTransaction);

        // Act
        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false
        });

        // Assert
        Assert.Equal(1, result.Executed);
        Assert.True(await IndexExistsAsync(schema, "users", "users_email_idx"));

        // Rollback should remove the concurrent index.
        await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = true
        });

        Assert.False(await IndexExistsAsync(schema, "users", "users_email_idx"));
    }

    [Fact]
    public async Task ApplyWorkflow_OnlineDdl_LeavesNonIndexStatementsInTransaction()
    {
        // Arrange
        var (fromDir, toDir, schema) = await CreateFromToWithAddedColumnAsync();

        var options = new MigrationOptions { FromDirectory = fromDir, ToDirectory = toDir, OnlineDdl = true };
        var plan = new MigrationPlanner().CreatePlan(options);

        // Assert: non-index (table/column) statements must not be rewritten to CONCURRENTLY
        // and must remain inside the transaction.
        var tableStatements = plan.Up.Where(s => s.Sql.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase) ||
                                                  s.Sql.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(tableStatements);
        Assert.All(tableStatements, s =>
        {
            Assert.DoesNotContain("CONCURRENTLY", s.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.False(s.RunsOutsideTransaction);
        });

        // Act: apply should still succeed
        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = _connectionString,
            Rollback = false
        });

        Assert.Equal(1, result.Executed);
        Assert.True(await ColumnExistsAsync(schema, "users", "email"));
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

    private async Task<bool> ColumnExistsAsync(string schema, string table, string column)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table AND column_name = @column;
        ";
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task<bool> TableExistsAsync(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table;
        ";
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task<bool> IndexExistsAsync(string schema, string table, string indexName)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1 FROM pg_indexes
            WHERE schemaname = @schema AND tablename = @table AND indexname = @index;
        ";
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@index", indexName);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task ExportSchemaAsync(string outputDir, string schema, string[]? includeTables = null)
    {
        Directory.CreateDirectory(outputDir);

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = outputDir,
            Schemas = [schema],
            Include = new IncludeOptions
            {
                Schemas = true,
                Tables = true,
                Constraints = false,
                Indexes = true,
                Views = false,
                Functions = false,
                Triggers = false,
                EventTriggers = false,
                Rules = false,
                Aggregates = false,
                Operators = false,
                Casts = false,
                Publications = false,
                Subscriptions = false,
                Policies = false,
                Comments = false,
                Grants = false,
                Extensions = false,
                Types = false,
                Sequences = false,
                Domains = false,
                ForeignTables = false
            }
        };

        var exporter = new SchemaExporter(
            provider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await exporter.ExportAsync(options);
    }

    private async Task<(string FromDir, string ToDir, string Schema)> CreateFromToWithAddedColumnAsync(string? schema = null, string addedColumnName = "email")
    {
        schema ??= await CreateSchemaAsync();

        var fromDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"), "from");
        var toDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"), "to");

        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer, {SqlIdentifier.Quote("name")} character varying(100));");
        await ExportSchemaAsync(fromDir, schema);

        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN {SqlIdentifier.Quote(addedColumnName)} character varying(255);");
        await ExportSchemaAsync(toDir, schema);

        // Reset the database to the baseline state so apply-up can be exercised.
        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} DROP COLUMN {SqlIdentifier.Quote(addedColumnName)};");

        return (fromDir, toDir, schema);
    }

    private async Task<(string FromDir, string ToDir, string Schema)> CreateFromToWithTableRemovalAsync(string? schema = null)
    {
        schema ??= await CreateSchemaAsync();

        var fromDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"), "from");
        var toDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"), "to");

        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        await ExportSchemaAsync(fromDir, schema);

        Directory.CreateDirectory(toDir);
        Directory.CreateDirectory(Path.Combine(toDir, "schemas"));
        File.Copy(
            Path.Combine(fromDir, "schemas", $"{schema}.sql"),
            Path.Combine(toDir, "schemas", $"{schema}.sql"));

        return (fromDir, toDir, schema);
    }

    private async Task<(string FromDir, string ToDir, string Schema)> CreateFromToWithAddedIndexAsync(string? schema = null, string indexName = "users_email_idx")
    {
        schema ??= await CreateSchemaAsync();

        var fromDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"), "from");
        var toDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"), "to");

        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer, {SqlIdentifier.Quote("email")} character varying(255));");
        await ExportSchemaAsync(fromDir, schema);

        await ExecuteNonQueryAsync($"CREATE INDEX {SqlIdentifier.Quote(indexName)} ON {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("email")});");
        await ExportSchemaAsync(toDir, schema);

        // Remove the index so the database is in the baseline state for apply-up.
        await ExecuteNonQueryAsync($"DROP INDEX {SqlIdentifier.Qualified(schema, indexName)};");

        return (fromDir, toDir, schema);
    }
}
