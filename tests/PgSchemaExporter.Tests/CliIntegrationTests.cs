using System.Diagnostics;
using System.Text;
using Npgsql;
using PgSchemaExporter.Core.Scripting;
using Testcontainers.PostgreSql;
using Xunit;

namespace PgSchemaExporter.Tests;

[Collection("PostgreSQL Integration")]
public class CliIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private string _connectionString = string.Empty;
    private readonly string _tempRoot;

    public CliIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-cli-" + Guid.NewGuid().ToString("n"));
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
    public async Task VersionFlag_PrintsVersion()
    {
        var result = await RunCli("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pgschema-export", result.Output);
    }

    [Fact]
    public async Task HelpFlag_PrintsUsage()
    {
        var result = await RunCli("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output);
    }

    [Fact]
    public async Task InitCommand_CreatesConfigTemplate()
    {
        var configPath = Path.Combine(_tempRoot, "pgschema-export.json");

        var result = await RunCli("init", "--output", configPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(configPath));
        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("connectionString", content);
    }

    [Fact]
    public async Task ExportCommand_CreatesSchemaFiles()
    {
        var schema = await CreateSchemaAsync();
        var outputDir = Path.Combine(_tempRoot, "export");

        var result = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", outputDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas");

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(outputDir));
        Assert.True(File.Exists(Path.Combine(outputDir, "schemas", $"{schema}.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "tables", $"{schema}.users.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "deploy.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "README.md")));
    }

    [Fact]
    public async Task ExportCommand_WithConfig_CreatesSchemaFiles()
    {
        var schema = await CreateSchemaAsync();
        var configPath = Path.Combine(_tempRoot, "config.json");
        var outputDir = Path.Combine(_tempRoot, "export-config");

        await File.WriteAllTextAsync(configPath, $@"
{{
    ""connectionString"": ""{_connectionString}"",
    ""outputDirectory"": ""{outputDir.Replace("\\", "\\\\")}"",
    ""schemas"": [""{schema}""],
    ""excludeSchemas"": [""pg_catalog"", ""information_schema""],
    ""include"": {{ ""tables"": true, ""schemas"": true }}
}}");

        var result = await RunCli("export", "--config", configPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(outputDir, "tables", $"{schema}.users.sql")));
    }

    [Fact]
    public async Task DiffCommand_DetectsAddedTable()
    {
        var leftDir = Path.Combine(_tempRoot, "left");
        var rightDir = Path.Combine(_tempRoot, "right");
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);
        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(leftDir, "tables", "common.sql"), "CREATE TABLE common (id int);");
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE common (id int);");
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "added.sql"), "CREATE TABLE added (id int);");

        var result = await RunCli("diff", "--left", leftDir, "--right", rightDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("tables/added.sql", result.Output);
    }

    [Fact]
    public async Task DiffCommand_WritesReportFile()
    {
        var leftDir = Path.Combine(_tempRoot, "left");
        var rightDir = Path.Combine(_tempRoot, "right");
        var reportPath = Path.Combine(_tempRoot, "report.txt");

        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);
        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(leftDir, "tables", "common.sql"), "CREATE TABLE common (id int);");
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE common (id int);");
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "added.sql"), "CREATE TABLE added (id int);");

        var result = await RunCli(
            "diff",
            "--left", leftDir,
            "--right", rightDir,
            "--output", reportPath,
            "--format", "text");

        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(reportPath));
        var content = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("Schema diff report", content);
    }

    [Fact]
    public async Task MigrateCommand_GeneratesMigrationFiles()
    {
        var fromDir = Path.Combine(_tempRoot, "from");
        var toDir = Path.Combine(_tempRoot, "to");
        var migrationsDir = Path.Combine(_tempRoot, "migrations");

        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(fromDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "orders.sql"), "CREATE TABLE orders (id int);");

        var result = await RunCli(
            "migrate",
            "--from", fromDir,
            "--to", toDir,
            "--output", migrationsDir,
            "--name", "add_orders");

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(migrationsDir));
        Assert.True(Directory.GetFiles(migrationsDir, "*.up.sql").Length == 1);
        Assert.True(Directory.GetFiles(migrationsDir, "*.down.sql").Length == 1);
        Assert.True(File.Exists(Path.Combine(migrationsDir, "history.json")));

        var upContent = await File.ReadAllTextAsync(Directory.GetFiles(migrationsDir, "*.up.sql")[0]);
        Assert.Contains("CREATE TABLE", upContent);
    }

    [Fact]
    public async Task PlanAndApplyCommand_DryRunAppliesMigration()
    {
        var schema = await CreateSchemaAsync();
        var fromDir = Path.Combine(_tempRoot, "plan-from");
        var toDir = Path.Combine(_tempRoot, "plan-to");
        var planPath = Path.Combine(_tempRoot, "plan.json");

        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(fromDir, "tables", $"{schema}.users.sql"), "CREATE TABLE \"users\" (\"id\" integer);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", $"{schema}.users.sql"), "CREATE TABLE \"users\" (\"id\" integer, \"email\" character varying(255));");

        var planResult = await RunCli(
            "plan",
            "--from", fromDir,
            "--to", toDir,
            "--output", planPath,
            "--format", "json");

        // Exit code 2 means there are pending changes.
        Assert.Equal(2, planResult.ExitCode);
        Assert.True(File.Exists(planPath));

        var applyResult = await RunCli(
            "apply",
            "--plan", planPath,
            "--connection", _connectionString,
            "--dry-run");

        Assert.Equal(0, applyResult.ExitCode);
        Assert.Contains("Dry run complete", applyResult.Output);
    }

    [Fact]
    public async Task DriftCommand_DetectsDrift()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = Path.Combine(_tempRoot, "drift-export");

        var exportResult = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", exportDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas");

        Assert.Equal(0, exportResult.ExitCode);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN email character varying(255);";
        await cmd.ExecuteNonQueryAsync();

        var result = await RunCli(
            "drift",
            "--schema", exportDir,
            "--connection", _connectionString,
            "--schemas", schema);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Drift detected", result.Error);
    }

    [Fact]
    public async Task FingerprintCommand_ComputesAndVerifiesFingerprint()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = Path.Combine(_tempRoot, "fingerprint");
        var fingerprintPath = Path.Combine(_tempRoot, "schema.fingerprint.json");

        var exportResult = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", exportDir,
            "--schemas", schema,
            "--include-tables");

        Assert.Equal(0, exportResult.ExitCode);

        var computeResult = await RunCli(
            "fingerprint",
            "--schema", exportDir,
            "--output", fingerprintPath);

        Assert.Equal(0, computeResult.ExitCode);
        Assert.True(File.Exists(fingerprintPath));

        var verifyResult = await RunCli(
            "fingerprint",
            "--schema", exportDir,
            "--verify", fingerprintPath);

        Assert.Equal(0, verifyResult.ExitCode);
        Assert.Contains("Fingerprint OK", verifyResult.Output);
    }

    [Fact]
    public async Task DiagramCommand_GeneratesDiagramFile()
    {
        var schema = await CreateSchemaAsync();
        var outputFile = Path.Combine(_tempRoot, "diagram.mmd");

        var result = await RunCli(
            "diagram",
            "--connection", _connectionString,
            "--schemas", schema,
            "--format", "mermaid",
            "--output", outputFile);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputFile));
        var content = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("erDiagram", content);
    }

    [Fact]
    public async Task SplitDumpCommand_SplitsDumpFile()
    {
        var dumpPath = Path.Combine(_tempRoot, "dump.sql");
        var outputDir = Path.Combine(_tempRoot, "split-out");

        await File.WriteAllTextAsync(dumpPath, @"
CREATE TABLE public.users (id int);
CREATE INDEX users_id_idx ON public.users (id);
CREATE OR REPLACE VIEW public.active_users AS SELECT id FROM public.users;
");

        var result = await RunCli(
            "split-dump",
            "--input", dumpPath,
            "--output", outputDir);

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(outputDir));
        Assert.True(File.Exists(Path.Combine(outputDir, "tables", "public.users.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "indexes", "public.users.indexes.sql")));
        Assert.True(File.Exists(Path.Combine(outputDir, "views", "public.active_users.sql")));
    }

    [Fact]
    public async Task WatchCommand_EmitsDiffOnFileChange()
    {
        var leftDir = Path.Combine(_tempRoot, "watch-left");
        var rightDir = Path.Combine(_tempRoot, "watch-right");
        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);
        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(leftDir, "tables", "common.sql"), "CREATE TABLE common (id int);");
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE common (id int);");

        var result = await RunWatch(
            ["watch", "--left", leftDir, "--right", rightDir],
            async () => await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "added.sql"), "CREATE TABLE added (id int);"));

        Assert.Contains("tables/added.sql", result.Output);
        Assert.Contains("Schema diff", result.Output);
    }

    [Fact]
    public async Task VerboseFlag_PrintsProgress()
    {
        var schema = await CreateSchemaAsync();
        var outputDir = Path.Combine(_tempRoot, "export-verbose");

        var result = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", outputDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas",
            "--verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("    [", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuietFlag_DoesNotPrintProgress()
    {
        var schema = await CreateSchemaAsync();
        var outputDir = Path.Combine(_tempRoot, "export-quiet");

        var result = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", outputDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas",
            "--quiet");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("==> ", result.Error);
        Assert.DoesNotContain("    [", result.Error);
    }

    [Fact]
    public async Task ProfileFlag_PrintsTimingSummary()
    {
        var schema = await CreateSchemaAsync();
        var outputDir = Path.Combine(_tempRoot, "export-profile");

        var result = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", outputDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas",
            "--quiet",
            "--profile");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Performance profile:", result.Error);
        Assert.Contains("total", result.Error);
    }

    [Fact]
    public async Task FingerprintCommand_VerifyMismatch_ReturnsExitCode2()
    {
        var exportDir = Path.Combine(_tempRoot, "fingerprint-mismatch");
        Directory.CreateDirectory(Path.Combine(exportDir, "tables"));
        await File.WriteAllTextAsync(Path.Combine(exportDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        var fingerprintPath = Path.Combine(_tempRoot, "mismatch.fingerprint.json");

        var computeResult = await RunCli("fingerprint", "--schema", exportDir, "--output", fingerprintPath);
        Assert.Equal(0, computeResult.ExitCode);

        await File.WriteAllTextAsync(Path.Combine(exportDir, "tables", "orders.sql"), "CREATE TABLE orders (id int);");

        var verifyResult = await RunCli("fingerprint", "--schema", exportDir, "--verify", fingerprintPath);
        Assert.Equal(2, verifyResult.ExitCode);
        Assert.Contains("MISMATCH", verifyResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders.sql", verifyResult.Error);
    }

    [Fact]
    public async Task PlanCommand_WithChanges_ReturnsExitCode2()
    {
        var fromDir = Path.Combine(_tempRoot, "plan-from-changes");
        var toDir = Path.Combine(_tempRoot, "plan-to-changes");
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(fromDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "orders.sql"), "CREATE TABLE orders (id int);");

        var result = await RunCli("plan", "--from", fromDir, "--to", toDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Migration plan", result.Output);
        Assert.Contains("orders", result.Output);
    }

    [Fact]
    public async Task PlanCommand_NoChanges_ReturnsExitCode0()
    {
        var fromDir = Path.Combine(_tempRoot, "plan-from-same");
        var toDir = Path.Combine(_tempRoot, "plan-to-same");
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(fromDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "users.sql"), "CREATE TABLE users (id int);");

        var result = await RunCli("plan", "--from", fromDir, "--to", toDir);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No changes", result.Output);
    }

    [Fact]
    public async Task DriftCommand_WritesJsonReport()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = Path.Combine(_tempRoot, "drift-json");
        var reportPath = Path.Combine(_tempRoot, "drift.json");

        var exportResult = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", exportDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas");

        Assert.Equal(0, exportResult.ExitCode);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN email character varying(255);";
        await cmd.ExecuteNonQueryAsync();

        var result = await RunCli(
            "drift",
            "--schema", exportDir,
            "--connection", _connectionString,
            "--schemas", schema,
            "--format", "json",
            "--output", reportPath);

        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(reportPath));
        var content = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("\"hasDifferences\"", content);
        Assert.Contains("\"added\"", content);
    }

    [Fact]
    public async Task DriftCommand_WritesHtmlReport()
    {
        var schema = await CreateSchemaAsync();
        var exportDir = Path.Combine(_tempRoot, "drift-html");
        var reportPath = Path.Combine(_tempRoot, "drift.html");

        var exportResult = await RunCli(
            "export",
            "--connection", _connectionString,
            "--output", exportDir,
            "--schemas", schema,
            "--include-tables",
            "--include-schemas");

        Assert.Equal(0, exportResult.ExitCode);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {SqlIdentifier.Qualified(schema, "users")} ADD COLUMN email character varying(255);";
        await cmd.ExecuteNonQueryAsync();

        var result = await RunCli(
            "drift",
            "--schema", exportDir,
            "--connection", _connectionString,
            "--schemas", schema,
            "--format", "html",
            "--output", reportPath);

        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(reportPath));
        var content = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("Schema diff report", content);
    }

    [Fact]
    public async Task MigrateCommand_WarnHazards()
    {
        var fromDir = Path.Combine(_tempRoot, "migrate-from");
        var toDir = Path.Combine(_tempRoot, "migrate-to");
        var outputDir = Path.Combine(_tempRoot, "migrations-hazards");
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        await File.WriteAllTextAsync(Path.Combine(fromDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        Directory.CreateDirectory(toDir);

        var result = await RunCli(
            "migrate",
            "--from", fromDir,
            "--to", toDir,
            "--output", outputDir,
            "--warn-hazards",
            "--name", "drop_users");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hazards detected", result.Output);
        Assert.Contains("TableDrop", result.Output);
    }

    [Fact]
    public async Task MigrateCommand_Preview()
    {
        var fromDir = Path.Combine(_tempRoot, "migrate-preview-from");
        var toDir = Path.Combine(_tempRoot, "migrate-preview-to");
        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));
        await File.WriteAllTextAsync(Path.Combine(fromDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "users.sql"), "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(Path.Combine(toDir, "tables", "orders.sql"), "CREATE TABLE orders (id int);");

        var result = await RunCli(
            "migrate",
            "--from", fromDir,
            "--to", toDir,
            "--preview");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CREATE TABLE", result.Output);
        Assert.Contains("DROP TABLE", result.Output);
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "migrations-preview")));
    }

    private async Task<string> CreateSchemaAsync()
    {
        var schema = "s" + Guid.NewGuid().ToString("n")[..8];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA {SqlIdentifier.Quote(schema)}; CREATE TABLE {SqlIdentifier.Qualified(schema, "users")} (id int);";
        await cmd.ExecuteNonQueryAsync();

        return schema;
    }

    private async Task<CliResult> RunCli(params string[] args)
    {
        var dllPath = FindCliDll();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _tempRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(dllPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cts.Token));

        var output = await outputTask;
        var error = await errorTask;

        return new CliResult(process.ExitCode, output, error);
    }

    private async Task<CliResult> RunWatch(string[] args, Func<Task> triggerAction)
    {
        var dllPath = FindCliDll();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = _tempRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(dllPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await Task.Delay(TimeSpan.FromSeconds(1));
        await triggerAction();
        await Task.Delay(TimeSpan.FromSeconds(2));

        process.Kill();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cts.Token));

        var output = await outputTask;
        var error = await errorTask;

        return new CliResult(process.ExitCode, output, error);
    }

    private static string FindCliDll()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "pgschema-export.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "pgschema-export", "pgschema-export.dll"),
            Path.Combine(AppContext.BaseDirectory, "PgSchemaExporter.Cli.dll"),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var search = Path.Combine(dir, "src", "PgSchemaExporter.Cli");
            if (Directory.Exists(search))
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var full = Path.Combine(search, "bin", config, "net8.0", "pgschema-export.dll");
                    if (File.Exists(full))
                        return Path.GetFullPath(full);
                }
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }

        throw new FileNotFoundException("pgschema-export CLI DLL was not found.");
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
}
