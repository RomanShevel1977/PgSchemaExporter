using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Diagramming;
using PgSchemaExporter.Core.Drift;
using PgSchemaExporter.Core.Integrity;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class DiagramDriftFingerprintIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = "";
    private readonly string _tempRoot;

    public DiagramDriftFingerprintIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-drift-diagram-" + Guid.NewGuid().ToString("n"));
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
    public async Task Diagram_FromLiveDatabase_GeneratesMermaid()
    {
        var schema = await CreateSchemaWithRelationshipAsync();

        var options = new DiagramOptions
        {
            ConnectionString = _connectionString,
            Schemas = [schema],
            Format = DiagramFormat.Mermaid
        };

        var generator = new SchemaDiagramGenerator();
        var diagram = await generator.GenerateAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Contains("erDiagram", diagram);
        Assert.Contains("users", diagram, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", diagram, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagram_FromLiveDatabase_GeneratesDot()
    {
        var schema = await CreateSchemaWithRelationshipAsync();

        var options = new DiagramOptions
        {
            ConnectionString = _connectionString,
            Schemas = [schema],
            Format = DiagramFormat.Dot
        };

        var generator = new SchemaDiagramGenerator();
        var diagram = await generator.GenerateAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Contains("digraph schema", diagram);
        Assert.Contains("users", diagram, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", diagram, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagram_FromExportedDirectory_GeneratesMermaid()
    {
        var schema = await CreateSchemaWithRelationshipAsync();
        var exportDir = await ExportSchemaAsync(schema);

        var options = new DiagramOptions
        {
            SchemaDirectory = exportDir,
            Format = DiagramFormat.Mermaid
        };

        var generator = new SchemaDiagramGenerator();
        var diagram = await generator.GenerateAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Contains("erDiagram", diagram);
        Assert.Contains("users", diagram, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", diagram, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Drift_NoDrift_WhenSchemaMatches()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = await ExportSchemaAsync(schema);

        var options = new DriftOptions
        {
            SchemaDirectory = exportDir,
            ConnectionString = _connectionString,
            Schemas = [schema]
        };

        var detector = new DriftDetector();
        var result = await detector.DetectAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        var details = $"Unexpected: {string.Join(",", result.Unexpected)}; Missing: {string.Join(",", result.Missing)}; Modified: {string.Join(",", result.Modified)}";
        Assert.False(result.HasDrifted, details);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Unexpected);
        Assert.Empty(result.Modified);
    }

    [Fact]
    public async Task Drift_DetectsTableChanges()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = await ExportSchemaAsync(schema);

        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN {SqlIdentifier.Quote("email")} character varying(255);");

        var options = new DriftOptions
        {
            SchemaDirectory = exportDir,
            ConnectionString = _connectionString,
            Schemas = [schema]
        };

        var detector = new DriftDetector();
        var result = await detector.DetectAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.True(result.HasDrifted);
        Assert.Contains(result.Modified, x => x.Contains("tables", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Fingerprint_ComputeAndVerify_Matches()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = await ExportSchemaAsync(schema);

        var computed = SchemaFingerprint.Compute(exportDir);
        Assert.NotEmpty(computed.Fingerprint);
        Assert.True(computed.FileCount > 0);

        var fingerprintPath = Path.Combine(_tempRoot, "schema.fingerprint.json");
        await SchemaFingerprintFile.WriteAsync(fingerprintPath, computed);

        var manifest = await SchemaFingerprintFile.ReadAsync(fingerprintPath);
        Assert.Equal(computed.Fingerprint, manifest.Fingerprint);

        var comparison = SchemaFingerprint.CompareFiles(manifest.Files, computed);
        Assert.False(comparison.HasDifferences);
    }

    [Fact]
    public async Task Fingerprint_DetectsAddedFile()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = await ExportSchemaAsync(schema);

        var before = SchemaFingerprint.Compute(exportDir);
        await SchemaFingerprintFile.WriteAsync(Path.Combine(_tempRoot, "schema.fingerprint.json"), before);

        File.WriteAllText(Path.Combine(exportDir, "extra.sql"), "SELECT 1;");

        var after = SchemaFingerprint.Compute(exportDir);
        var manifest = await SchemaFingerprintFile.ReadAsync(Path.Combine(_tempRoot, "schema.fingerprint.json"));
        var comparison = SchemaFingerprint.CompareFiles(manifest.Files, after);

        Assert.True(comparison.HasDifferences);
        Assert.Contains("extra.sql", comparison.Added);
    }

    private async Task<string> CreateSchemaAsync()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE SCHEMA {SqlIdentifier.Quote(schema)};");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer PRIMARY KEY, {SqlIdentifier.Quote("name")} character varying(100));");
        return schema;
    }

    private async Task<string> CreateSchemaWithRelationshipAsync()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE SCHEMA {SqlIdentifier.Quote(schema)};");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer PRIMARY KEY, {SqlIdentifier.Quote("name")} character varying(100));");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "orders")} ({SqlIdentifier.Quote("id")} integer PRIMARY KEY, {SqlIdentifier.Quote("user_id")} integer NOT NULL REFERENCES {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")}));");
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

    private async Task<string> ExportSchemaAsync(string schema)
    {
        var outputDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(outputDir);

        var provider = new PostgresMetadataProvider();
        var options = new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = outputDir,
            Schemas = [schema]
        };

        var model = await provider.LoadAsync(_connectionString, options, NullProgressReporter.Instance, NullLogger.Instance);
        await new SchemaFileWriter().WriteAsync(outputDir, model, options.Format);

        return outputDir;
    }
}
