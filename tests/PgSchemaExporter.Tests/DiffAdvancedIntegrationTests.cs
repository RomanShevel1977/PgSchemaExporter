using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class DiffAdvancedIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = "";
    private readonly string _tempRoot;

    public DiffAdvancedIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-diff-" + Guid.NewGuid().ToString("n"));
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
    public async Task Diff_Directories_DetectsAddedRemovedAndChanged()
    {
        var (leftDir, rightDir) = CreateDirectoriesWithDifference();

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = leftDir,
            RightDirectory = rightDir,
            Format = DiffFormat.Text
        });

        Assert.True(result.HasDifferences);
        Assert.Contains("tables/common.sql", result.Changed);
        Assert.Contains("tables/added.sql", result.Added);
        Assert.Contains("tables/removed.sql", result.Removed);
        Assert.Single(result.Statistics);
        Assert.Equal(1, result.Statistics[0].Added);
        Assert.Equal(1, result.Statistics[0].Removed);
        Assert.Equal(1, result.Statistics[0].Changed);
    }

    [Fact]
    public async Task Diff_IgnoreComments_TreatsAsEqual()
    {
        var (leftDir, rightDir) = CreateDirectoriesWithCommentDifference();

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = leftDir,
            RightDirectory = rightDir,
            IgnoreComments = true
        });

        Assert.False(result.HasDifferences);
        Assert.Empty(result.Changed);
    }

    [Fact]
    public async Task Diff_IgnoreWhitespace_TreatsAsEqual()
    {
        var (leftDir, rightDir) = CreateDirectoriesWithWhitespaceDifference();

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = leftDir,
            RightDirectory = rightDir,
            IgnoreWhitespace = true
        });

        Assert.False(result.HasDifferences);
        Assert.Empty(result.Changed);
    }

    [Fact]
    public async Task Diff_ShowContext_PopulatesFileDiffs()
    {
        var (leftDir, rightDir) = CreateDirectoriesWithDifference();

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = leftDir,
            RightDirectory = rightDir,
            ShowContext = true
        });

        Assert.NotEmpty(result.FileDiffs);
        Assert.Contains(result.FileDiffs, f => f.Path == "tables/common.sql");
        Assert.Contains(result.FileDiffs[0].Lines, l => l.Kind == DiffLineKind.Added);
        Assert.Contains(result.FileDiffs[0].Lines, l => l.Kind == DiffLineKind.Removed);
    }

    [Fact]
    public async Task Diff_ReportWriter_WritesJsonAndHtml()
    {
        var (leftDir, rightDir) = CreateDirectoriesWithDifference();
        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = leftDir,
            RightDirectory = rightDir,
            ShowContext = true
        });

        var writer = new SchemaDiffReportWriter();

        var json = writer.BuildReport(result, DiffFormat.Json);
        Assert.Contains("\"hasDifferences\"", json);
        Assert.Contains("\"added\"", json);

        var html = writer.BuildReport(result, DiffFormat.Html);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Schema diff report", html);
    }

    [Fact]
    public async Task DiffAsync_DirectoryVsLiveDatabase_DetectsDrift()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE SCHEMA {SqlIdentifier.Quote(schema)};");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");

        var baselineDir = await ExportDatabaseAsync(schema);

        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN {SqlIdentifier.Quote("email")} character varying(255);");

        var differ = new SchemaDiffer();
        var result = await differ.DiffAsync(new SchemaDiffOptions
        {
            LeftDirectory = baselineDir,
            RightConnectionString = _connectionString,
            Schemas = [schema],
            Format = DiffFormat.Text
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Changed, x => x.Contains("tables", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiffAsync_WithLiveDbAndExcludeSchemas_ExcludesParallelRunsWithoutError()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];
        var otherSchema = "other" + Guid.NewGuid().ToString("n")[..8];
        await ExecuteNonQueryAsync($"CREATE SCHEMA {SqlIdentifier.Quote(schema)};");
        await ExecuteNonQueryAsync($"CREATE SCHEMA {SqlIdentifier.Quote(otherSchema)};");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} ({SqlIdentifier.Quote("id")} integer);");
        await ExecuteNonQueryAsync($"CREATE TABLE {SqlIdentifier.Qualified(otherSchema, "other_table")} ({SqlIdentifier.Quote("id")} integer);");

        var baselineDir = await ExportDatabaseAsync(schema);

        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN {SqlIdentifier.Quote("email")} character varying(255);");
        await ExecuteNonQueryAsync($"ALTER TABLE {SqlIdentifier.Qualified(otherSchema, "other_table")} ADD COLUMN {SqlIdentifier.Quote("name")} character varying(255);");

        var differ = new SchemaDiffer();
        var result = await differ.DiffAsync(new SchemaDiffOptions
        {
            LeftDirectory = baselineDir,
            RightConnectionString = _connectionString,
            Schemas = [schema, otherSchema],
            ExcludeSchemas = [otherSchema],
            Parallel = true,
            Format = DiffFormat.Text
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.True(result.HasDifferences);
        Assert.Contains(result.Changed, x => x.Contains("tables", StringComparison.OrdinalIgnoreCase));
        // The excluded schema should not be part of the diff.
        Assert.DoesNotContain(result.Changed, x => x.Contains("other_table", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Added, x => x.Contains("other_table", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> ExportDatabaseAsync(string schema)
    {
        var outputDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("n"));
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
                Indexes = false,
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

        await exporter.ExportAsync(options, NullProgressReporter.Instance, NullLogger.Instance);
        return outputDir;
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private (string LeftDir, string RightDir) CreateDirectoriesWithDifference()
    {
        var leftDir = Path.Combine(_tempRoot, "left" + Guid.NewGuid().ToString("n"));
        var rightDir = Path.Combine(_tempRoot, "right" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);

        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        File.WriteAllText(Path.Combine(leftDir, "tables", "common.sql"), "CREATE TABLE users (id integer);");
        File.WriteAllText(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE users (id integer, email text);");

        File.WriteAllText(Path.Combine(leftDir, "tables", "removed.sql"), "CREATE TABLE removed (id integer);");
        File.WriteAllText(Path.Combine(rightDir, "tables", "added.sql"), "CREATE TABLE added (id integer);");

        return (leftDir, rightDir);
    }

    private (string LeftDir, string RightDir) CreateDirectoriesWithCommentDifference()
    {
        var leftDir = Path.Combine(_tempRoot, "left-comment" + Guid.NewGuid().ToString("n"));
        var rightDir = Path.Combine(_tempRoot, "right-comment" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);

        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        File.WriteAllText(Path.Combine(leftDir, "tables", "common.sql"), "CREATE TABLE users (id integer);\n-- comment");
        File.WriteAllText(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE users (id integer);");

        return (leftDir, rightDir);
    }

    private (string LeftDir, string RightDir) CreateDirectoriesWithWhitespaceDifference()
    {
        var leftDir = Path.Combine(_tempRoot, "left-ws" + Guid.NewGuid().ToString("n"));
        var rightDir = Path.Combine(_tempRoot, "right-ws" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);

        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        File.WriteAllText(Path.Combine(leftDir, "tables", "common.sql"), "CREATE   TABLE users (id   integer);");
        File.WriteAllText(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE users (id integer);");

        return (leftDir, rightDir);
    }
}
