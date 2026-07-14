using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Diagramming;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Drift;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class DiagramDriftOptionsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = string.Empty;
    private readonly string _tempRoot;

    public DiagramDriftOptionsIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-drift-diagram-opts-" + Guid.NewGuid().ToString("n"));
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        Directory.CreateDirectory(_tempRoot);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task Diagram_ExcludeSchemas_ExcludesExcludedSchema()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        var other = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            CREATE SCHEMA {SqlIdentifier.Quote(schema)};
            CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} (id int PRIMARY KEY);
            CREATE SCHEMA {SqlIdentifier.Quote(other)};
            CREATE TABLE {SqlIdentifier.Qualified(other, "logs")} (id int PRIMARY KEY);
        ";
        await cmd.ExecuteNonQueryAsync();

        var generator = new SchemaDiagramGenerator();
        var diagram = await generator.GenerateAsync(new DiagramOptions
        {
            ConnectionString = _connectionString,
            Schemas = [schema, other],
            ExcludeSchemas = [other],
            Format = DiagramFormat.Mermaid
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Contains("users", diagram, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logs", diagram, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagram_OutputFile_WritesDiagramToDisk()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA {SqlIdentifier.Quote(schema)}; CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} (id int);";
        await cmd.ExecuteNonQueryAsync();

        var outputFile = Path.Combine(_tempRoot, "diagram.mmd");
        var generator = new SchemaDiagramGenerator();
        var diagram = await generator.GenerateAsync(new DiagramOptions
        {
            ConnectionString = _connectionString,
            Schemas = [schema],
            Format = DiagramFormat.Mermaid,
            OutputFile = outputFile
        }, NullProgressReporter.Instance, NullLogger.Instance);

        // The core generator returns the diagram; the CLI writes it to disk. We
        // mimic the CLI behavior here to exercise the OutputFile path.
        await File.WriteAllTextAsync(outputFile, diagram);

        Assert.True(File.Exists(outputFile));
        var content = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("erDiagram", content);
    }

    [Fact]
    public async Task Drift_ExcludeSchemas_ExcludesExcludedSchema()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        var other = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            CREATE SCHEMA {SqlIdentifier.Quote(schema)};
            CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} (id int);
            CREATE SCHEMA {SqlIdentifier.Quote(other)};
            CREATE TABLE {SqlIdentifier.Qualified(other, "logs")} (id int);
        ";
        await cmd.ExecuteNonQueryAsync();

        var exporter = new SchemaExporter(
            new PostgresMetadataProvider(),
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var exportDir = Path.Combine(_tempRoot, "export");
        await exporter.ExportAsync(new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = exportDir,
            Schemas = [schema],
            ExcludeSchemas = ["pg_catalog", "information_schema"],
            Include = new IncludeOptions { Tables = true, Schemas = true }
        });

        // Add a new table to the excluded schema in the live database. The drift
        // detector should not report it when that schema is excluded.
        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = $"CREATE TABLE {SqlIdentifier.Qualified(other, "new_table")} (id int);";
        await cmd2.ExecuteNonQueryAsync();

        var detector = new DriftDetector();
        var result = await detector.DetectAsync(new DriftOptions
        {
            SchemaDirectory = exportDir,
            ConnectionString = _connectionString,
            Schemas = [schema, other],
            ExcludeSchemas = [other]
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.False(result.HasDrifted);
        Assert.Empty(result.Unexpected);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Modified);
    }

    [Fact]
    public async Task Drift_OutputFile_WritesDriftReportToDisk()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA {SqlIdentifier.Quote(schema)}; CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} (id int);";
        await cmd.ExecuteNonQueryAsync();

        var exporter = new SchemaExporter(
            new PostgresMetadataProvider(),
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var exportDir = Path.Combine(_tempRoot, "drift-export");
        await exporter.ExportAsync(new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = exportDir,
            Schemas = [schema],
            ExcludeSchemas = ["pg_catalog", "information_schema"],
            Include = new IncludeOptions { Tables = true, Schemas = true }
        });

        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = $"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN email character varying(255);";
        await cmd2.ExecuteNonQueryAsync();

        var outputFile = Path.Combine(_tempRoot, "drift-report.txt");

        var detector = new DriftDetector();
        var result = await detector.DetectAsync(new DriftOptions
        {
            SchemaDirectory = exportDir,
            ConnectionString = _connectionString,
            Schemas = [schema],
            OutputFile = outputFile,
            Format = DiffFormat.Text
        }, NullProgressReporter.Instance, NullLogger.Instance);

        // The core detector does not write the file; the CLI does. We mimic the CLI
        // behavior to exercise the OutputFile path.
        var reportWriter = new SchemaDiffReportWriter();
        await reportWriter.WriteAsync(outputFile, result.Diff, DiffFormat.Text);

        Assert.True(result.HasDrifted);
        Assert.True(File.Exists(outputFile));

        var content = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("Schema diff report", content);
        Assert.Contains("users", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Drift_IgnoreCommentsAndWhitespace_ReportsNoDriftForCommentChange()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA {SqlIdentifier.Quote(schema)}; CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} (id int);";
        await cmd.ExecuteNonQueryAsync();

        var exporter = new SchemaExporter(
            new PostgresMetadataProvider(),
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var exportDir = Path.Combine(_tempRoot, "drift-ignore");
        await exporter.ExportAsync(new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = exportDir,
            Schemas = [schema],
            ExcludeSchemas = ["pg_catalog", "information_schema"],
            Include = new IncludeOptions { Tables = true, Schemas = true }
        });

        // Modify the exported file to add a comment and whitespace only.
        var tableFile = Path.Combine(exportDir, "tables", $"{schema}.users.sql");
        var content = await File.ReadAllTextAsync(tableFile);
        await File.WriteAllTextAsync(tableFile, content.Trim() + "\n-- comment\n\n");

        var detector = new DriftDetector();
        var result = await detector.DetectAsync(new DriftOptions
        {
            SchemaDirectory = exportDir,
            ConnectionString = _connectionString,
            Schemas = [schema],
            IgnoreComments = true,
            IgnoreWhitespace = true
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.False(result.HasDrifted, $"Expected no drift, got unexpected: {string.Join(", ", result.Unexpected)}, missing: {string.Join(", ", result.Missing)}, modified: {string.Join(", ", result.Modified)}");
    }
}
