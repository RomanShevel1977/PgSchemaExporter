using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class ExportOptionsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = "";
    private readonly string _tempRoot;

    public ExportOptionsIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-export-options-" + Guid.NewGuid().ToString("n"));
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
    public async Task CleanOutputDirectory_RemovesStaleFiles()
    {
        // Arrange
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");

        var outputDir = Path.Combine(_tempRoot, "clean");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "stale.sql"), "-- stale");

        // Act
        var exporter = CreateExporter();
        await exporter.ExportAsync(CreateExportOptions(outputDir, schema, new FormatOptions(), clean: true), NullProgressReporter.Instance, NullLogger.Instance);

        // Assert
        Assert.False(File.Exists(Path.Combine(outputDir, "stale.sql")));
        Assert.True(Directory.EnumerateFiles(outputDir, "*.sql", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task UseIfNotExistsFalse_RemovesIfNotExists()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");

        var outputDir = Path.Combine(_tempRoot, "no-if-not-exists");
        var options = CreateExportOptions(outputDir, schema, new FormatOptions { UseIfNotExists = false });

        var exporter = CreateExporter();
        await exporter.ExportAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        var tableSql = await File.ReadAllTextAsync(Path.Combine(outputDir, "tables", $"{schema}.users.sql"));
        Assert.DoesNotContain("IF NOT EXISTS", tableSql);
        Assert.Contains("CREATE TABLE", tableSql);
    }

    [Fact]
    public async Task SplitConstraintsFalse_InlinesConstraints()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"" +
            $"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer, {SqlIdentifier.Quote("email")} character varying(255));");
        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD CONSTRAINT {SqlIdentifier.Quote("uq_users_email")} UNIQUE ({SqlIdentifier.Quote("email")});");

        var outputDir = Path.Combine(_tempRoot, "inline-constraints");
        var options = CreateExportOptions(outputDir, schema, new FormatOptions { SplitConstraints = false });

        var exporter = CreateExporter();
        await exporter.ExportAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        var tableSql = await File.ReadAllTextAsync(Path.Combine(outputDir, "tables", $"{schema}.users.sql"));
        Assert.Contains("ALTER TABLE", tableSql);
        Assert.Contains("ADD CONSTRAINT", tableSql);
        Assert.False(Directory.Exists(Path.Combine(outputDir, "constraints")));
    }

    [Fact]
    public async Task SplitIndexesFalse_InlinesIndexes()
    {
        var schema = await CreateSchemaAsync();
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer, {SqlIdentifier.Quote("email")} character varying(255));");
        await ExecuteNonQueryAsync($"CREATE INDEX {SqlIdentifier.Quote("ix_users_email")} ON {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("email")});");

        var outputDir = Path.Combine(_tempRoot, "inline-indexes");
        var options = CreateExportOptions(outputDir, schema, new FormatOptions { SplitIndexes = false });

        var exporter = CreateExporter();
        await exporter.ExportAsync(options, NullProgressReporter.Instance, NullLogger.Instance);

        var tableSql = await File.ReadAllTextAsync(Path.Combine(outputDir, "tables", $"{schema}.users.sql"));
        Assert.Contains("CREATE INDEX", tableSql);
        Assert.False(Directory.Exists(Path.Combine(outputDir, "indexes")));
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

    private ExportOptions CreateExportOptions(string outputDir, string schema, FormatOptions format, bool clean = false)
    {
        return new ExportOptions
        {
            ConnectionString = _connectionString,
            OutputDirectory = outputDir,
            Schemas = [schema],
            CleanOutputDirectory = clean,
            Format = format,
            Include = new IncludeOptions
            {
                Schemas = true,
                Tables = true,
                Constraints = true,
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
    }

    private static SchemaExporter CreateExporter()
    {
        var provider = new PostgresMetadataProvider();
        return new SchemaExporter(
            provider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());
    }
}
