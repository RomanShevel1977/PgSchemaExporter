using PgSchemaExporter.Core.Configuration;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using PgSchemaExporter.Core;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class CliEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = string.Empty;
    private readonly string _tempRoot;

    public CliEndToEndTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-e2e-" + Guid.NewGuid().ToString("n"));
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        Directory.CreateDirectory(_tempRoot);

        await SetupTestDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ExportWorkflow_FullCycle()
    {
        // Arrange
        var outputDir = Path.Combine(_tempRoot, "export");
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = outputDir,
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Tables = true,
                Views = true,
                Functions = true,
                Indexes = true,
                Constraints = true
            }
        };

        // Act
        var exporter = new SchemaExporter(
            provider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var summary = await exporter.ExportAsync(options);

        // Assert
        Assert.True(Directory.Exists(outputDir));
        Assert.True(summary.TotalObjects > 0);
        Assert.Contains(summary.Counts, c => c.ObjectKind == "tables" && c.Count > 0);

        // Verify files were created
        var tablesDir = Path.Combine(outputDir, "schemas", "public", "tables");
        Assert.True(Directory.Exists(tablesDir));
        var tableFiles = Directory.GetFiles(tablesDir, "*.sql");
        Assert.NotEmpty(tableFiles);

        // Verify deploy script was created
        var deployScript = Path.Combine(outputDir, "deploy.sql");
        Assert.True(File.Exists(deployScript));

        // Verify README was created
        var readme = Path.Combine(outputDir, "README.md");
        Assert.True(File.Exists(readme));
    }

    [Fact]
    public async Task ExportWorkflow_WithParallelMode()
    {
        // Arrange
        var outputDir = Path.Combine(_tempRoot, "export-parallel");
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = outputDir,
            Schemas = ["public"],
            Parallel = true,
            Include = new IncludeOptions
            {
                Tables = true,
                Views = true,
                Functions = true
            }
        };

        // Act
        var exporter = new SchemaExporter(
            provider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var summary = await exporter.ExportAsync(options);

        // Assert
        Assert.True(Directory.Exists(outputDir));
        Assert.True(summary.TotalObjects > 0);
    }

    [Fact]
    public async Task ExportWorkflow_DryRun_DoesNotWriteFiles()
    {
        // Arrange
        var outputDir = Path.Combine(_tempRoot, "export-dryrun");
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = outputDir,
            Schemas = ["public"],
            DryRun = true,
            Include = new IncludeOptions { Tables = true }
        };

        // Act
        var exporter = new SchemaExporter(
            provider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var summary = await exporter.ExportAsync(options);

        // Assert
        Assert.True(summary.DryRun);
        Assert.False(Directory.Exists(outputDir));
        Assert.True(summary.TotalObjects > 0);
    }

    [Fact]
    public async Task DiffWorkflow_DirectoryToDirectory()
    {
        // Arrange
        var dir1 = Path.Combine(_tempRoot, "schema1");
        var dir2 = Path.Combine(_tempRoot, "schema2");

        await ExportToDirectoryAsync(dir1);
        await ExportToDirectoryAsync(dir2);

        // Add a change to dir2
        var tablesDir = Path.Combine(dir2, "schemas", "public", "tables");
        File.WriteAllText(
            Path.Combine(tablesDir, "new_table.sql"),
            "CREATE TABLE new_table (id int);");

        // Act
        var differ = new SchemaDiffer();
        var options = new SchemaDiffOptions
        {
            LeftDirectory = dir1,
            RightDirectory = dir2
        };
        var result = differ.Diff(options);

        // Assert
        Assert.True(result.HasDifferences);
        Assert.NotEmpty(result.Added);
        Assert.Contains(result.Added, f => f.Contains("new_table.sql"));
    }

    [Fact]
    public async Task DiffWorkflow_DirectoryToLiveDatabase()
    {
        // Arrange
        var dir = Path.Combine(_tempRoot, "schema");
        await ExportToDirectoryAsync(dir);

        // Add a new table to the live database
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE e2e_test_table (id int);";
        await cmd.ExecuteNonQueryAsync();

        // Act
        var differ = new SchemaDiffer();
        var options = new SchemaDiffOptions
        {
            LeftDirectory = dir,
            RightConnectionString = _connectionString
        };
        var result = await differ.DiffAsync(options);

        // Assert
        Assert.True(result.HasDifferences);
        Assert.NotEmpty(result.Added);
    }

    [Fact]
    public async Task DiffWorkflow_LiveDatabaseToLiveDatabase()
    {
        // Arrange
        // Create a second database
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE DATABASE testdb2;";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString);
        builder.Database = "testdb2";
        var connectionString2 = builder.ToString();

        // Add a table to the first database
        cmd.CommandText = "CREATE TABLE only_in_db1 (id int);";
        await cmd.ExecuteNonQueryAsync();

        // Act
        var differ = new SchemaDiffer();
        var options = new SchemaDiffOptions
        {
            LeftConnectionString = _connectionString,
            RightConnectionString = connectionString2
        };
        var result = await differ.DiffAsync(options);

        // Assert
        Assert.True(result.HasDifferences);
    }

    [Fact]
    public async Task MigrateWorkflow_GeneratesUpAndDownScripts()
    {
        // Arrange
        var fromDir = Path.Combine(_tempRoot, "from");
        var toDir = Path.Combine(_tempRoot, "to");
        var migrationsDir = Path.Combine(_tempRoot, "migrations");

        await ExportToDirectoryAsync(fromDir);

        // Add a change to toDir
        var tablesDir = Path.Combine(toDir, "schemas", "public", "tables");
        Directory.CreateDirectory(tablesDir);
        File.WriteAllText(
            Path.Combine(tablesDir, "new_table.sql"),
            "CREATE TABLE new_table (id int);");

        // Act
        var generator = new MigrationGenerator();
        var options = new MigrationOptions
        {
            FromDirectory = fromDir,
            ToDirectory = toDir,
            OutputDirectory = migrationsDir,
            Name = "test_migration"
        };
        var script = generator.Generate(options);

        var writer = new MigrationWriter();
        var result = await writer.WriteAsync(options, script, DateTimeOffset.UtcNow);

        // Assert
        Assert.True(File.Exists(result.UpFile));
        Assert.True(File.Exists(result.DownFile));

        var upContent = await File.ReadAllTextAsync(result.UpFile);
        var downContent = await File.ReadAllTextAsync(result.DownFile);

        Assert.Contains("CREATE TABLE", upContent);
        Assert.Contains("DROP TABLE", downContent);
    }

    [Fact]
    public async Task InitWorkflow_CreatesConfigTemplate()
    {
        // Arrange
        var configPath = Path.Combine(_tempRoot, "pgschema-export.json");

        // Act
        await ExportConfigWriter.WriteAsync(configPath);

        // Assert
        Assert.True(File.Exists(configPath));

        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("connectionString", content);
        Assert.Contains("outputDirectory", content);
        Assert.Contains("parallel", content);
        Assert.Contains("include", content);
    }

    [Fact]
    public async Task InitWorkflow_WithOverwrite_OverwritesExistingFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempRoot, "pgschema-export.json");
        await File.WriteAllTextAsync(configPath, "old content");

        // Act
        await ExportConfigWriter.WriteAsync(configPath, overwrite: true);

        // Assert
        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("connectionString", content);
        Assert.DoesNotContain("old content", content);
    }

    [Fact]
    public async Task InitWorkflow_WithoutOverwrite_ThrowsOnExistingFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempRoot, "pgschema-export.json");
        await File.WriteAllTextAsync(configPath, "old content");

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => ExportConfigWriter.WriteAsync(configPath));
    }

    [Fact]
    public async Task WatchWorkflow_EmitsInitialDiff()
    {
        // Arrange
        var leftDir = Path.Combine(_tempRoot, "watch-left");
        var rightDir = Path.Combine(_tempRoot, "watch-right");

        await ExportToDirectoryAsync(leftDir);
        await ExportToDirectoryAsync(rightDir);

        var watcher = new SchemaWatcher(debounceMilliseconds: 50);
        using var cts = new CancellationTokenSource();

        SchemaDiffResult? initialResult = null;
        var tcs = new TaskCompletionSource();

        // Act
        var watchTask = watcher.WatchAsync(
            new SchemaDiffOptions
            {
                LeftDirectory = leftDir,
                RightDirectory = rightDir
            },
            result =>
            {
                initialResult ??= result;
                tcs.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await watchTask; } catch (OperationCanceledException) { }

        // Assert
        Assert.NotNull(initialResult);
    }

    private async Task ExportToDirectoryAsync(string directory)
    {
        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = directory,
            Schemas = ["public"],
            Include = new IncludeOptions
            {
                Tables = true,
                Views = true,
                Functions = true,
                Indexes = true,
                Constraints = true
            }
        };

        var exporter = new SchemaExporter(
            provider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await exporter.ExportAsync(options);
    }

    private async Task SetupTestDatabaseAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
                email VARCHAR(255) NOT NULL UNIQUE,
                created_at TIMESTAMP DEFAULT NOW()
            );
            
            CREATE TABLE IF NOT EXISTS orders (
                id SERIAL PRIMARY KEY,
                user_id INTEGER REFERENCES users(id),
                total DECIMAL(10,2)
            );

            CREATE OR REPLACE VIEW user_summary AS
            SELECT id, email FROM users;

            CREATE OR REPLACE FUNCTION add_numbers(a integer, b integer)
            RETURNS integer AS $$
            BEGIN
                RETURN a + b;
            END;
            $$ LANGUAGE plpgsql;

            CREATE INDEX users_email_idx ON users(email);
        ";
        await cmd.ExecuteNonQueryAsync();
    }
}
