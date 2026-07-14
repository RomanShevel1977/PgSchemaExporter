using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationHistoryIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public MigrationHistoryIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-history-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task AppendAsync_WritesHistoryEntry()
    {
        var outputDir = Path.Combine(_tempRoot, "migrations");

        var entry = new MigrationHistoryEntry
        {
            AppliedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Name = "test_migration",
            UpFile = "20260102030405_test_migration.up.sql",
            DownFile = "20260102030405_test_migration.down.sql",
            UpStatements = 3,
            DownStatements = 2,
            Destructive = true
        };

        await MigrationHistory.AppendAsync(outputDir, entry);

        var historyPath = Path.Combine(outputDir, "history.json");
        Assert.True(File.Exists(historyPath));

        var history = await MigrationHistory.ReadAsync(historyPath);
        Assert.Single(history.Migrations);
        Assert.Equal("test_migration", history.Migrations[0].Name);
        Assert.Equal(3, history.Migrations[0].UpStatements);
        Assert.Equal(2, history.Migrations[0].DownStatements);
        Assert.True(history.Migrations[0].Destructive);
    }

    [Fact]
    public async Task AppendAsync_MultipleTimes_AppendsEntries()
    {
        var outputDir = Path.Combine(_tempRoot, "migrations");

        await MigrationHistory.AppendAsync(outputDir, new MigrationHistoryEntry { Name = "first" });
        await MigrationHistory.AppendAsync(outputDir, new MigrationHistoryEntry { Name = "second" });

        var history = await MigrationHistory.ReadAsync(Path.Combine(outputDir, "history.json"));

        Assert.Equal(2, history.Migrations.Count);
        Assert.Equal("first", history.Migrations[0].Name);
        Assert.Equal("second", history.Migrations[1].Name);
    }

    [Fact]
    public async Task FullMigrateWorkflow_GeneratesFilesAndHistory()
    {
        var fromDir = Path.Combine(_tempRoot, "from");
        var toDir = Path.Combine(_tempRoot, "to");
        var migrationsDir = Path.Combine(_tempRoot, "migrations");

        Directory.CreateDirectory(Path.Combine(fromDir, "tables"));
        Directory.CreateDirectory(Path.Combine(toDir, "tables"));

        await File.WriteAllTextAsync(
            Path.Combine(fromDir, "tables", "users.sql"),
            "CREATE TABLE users (id int);");

        await File.WriteAllTextAsync(
            Path.Combine(toDir, "tables", "users.sql"),
            "CREATE TABLE users (id int);");
        await File.WriteAllTextAsync(
            Path.Combine(toDir, "tables", "orders.sql"),
            "CREATE TABLE orders (id int);");

        var generator = new MigrationGenerator();
        var options = new MigrationOptions
        {
            FromDirectory = fromDir,
            ToDirectory = toDir,
            OutputDirectory = migrationsDir,
            Name = "add_orders"
        };

        var script = generator.Generate(options);
        var writer = new MigrationWriter();
        var result = await writer.WriteAsync(options, script, DateTimeOffset.UtcNow);

        // Simulate the history append that the CLI migrate command performs.
        await MigrationHistory.AppendAsync(options.OutputDirectory, new MigrationHistoryEntry
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Name = options.Name,
            UpFile = Path.GetFileName(result.UpFile),
            DownFile = Path.GetFileName(result.DownFile),
            UpStatements = script.Up.Count,
            DownStatements = script.Down.Count,
            Destructive = script.HasDestructiveChanges
        });

        var history = await MigrationHistory.ReadAsync(Path.Combine(migrationsDir, "history.json"));

        Assert.True(File.Exists(result.UpFile));
        Assert.True(File.Exists(result.DownFile));
        Assert.Single(history.Migrations);
        Assert.Equal("add_orders", history.Migrations[0].Name);
        Assert.Equal(script.Up.Count, history.Migrations[0].UpStatements);
        Assert.Equal(script.Down.Count, history.Migrations[0].DownStatements);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsEmptyHistory()
    {
        var history = await MigrationHistory.ReadAsync(Path.Combine(_tempRoot, "missing.json"));
        Assert.Empty(history.Migrations);
    }

    [Fact]
    public async Task ReadAsync_EmptyFile_ReturnsEmptyHistory()
    {
        var path = Path.Combine(_tempRoot, "empty.json");
        await File.WriteAllTextAsync(path, "   ");

        var history = await MigrationHistory.ReadAsync(path);
        Assert.Empty(history.Migrations);
    }
}
