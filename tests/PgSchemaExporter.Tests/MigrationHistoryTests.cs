using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationHistoryTests : IDisposable
{
    private readonly string _root;

    public MigrationHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-hist-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Read_MissingFile_ReturnsEmpty()
    {
        var history = await MigrationHistory.ReadAsync(Path.Combine(_root, "history.json"));

        Assert.Empty(history.Migrations);
    }

    [Fact]
    public async Task Append_CreatesFileAndEntry()
    {
        await MigrationHistory.AppendAsync(_root, new MigrationHistoryEntry
        {
            AppliedAt = DateTimeOffset.UtcNow,
            Name = "add_users",
            UpFile = "20260101_add_users.up.sql",
            DownFile = "20260101_add_users.down.sql",
            UpStatements = 3,
            DownStatements = 3,
            Destructive = false
        });

        var path = Path.Combine(_root, MigrationHistory.DefaultFileName);
        Assert.True(File.Exists(path));

        var history = await MigrationHistory.ReadAsync(path);
        var entry = Assert.Single(history.Migrations);
        Assert.Equal("add_users", entry.Name);
        Assert.Equal(3, entry.UpStatements);
        Assert.False(entry.Destructive);
    }

    [Fact]
    public async Task Append_MultipleEntries_PreservesOrder()
    {
        await MigrationHistory.AppendAsync(_root, new MigrationHistoryEntry { Name = "first", UpFile = "1.up.sql", DownFile = "1.down.sql" });
        await MigrationHistory.AppendAsync(_root, new MigrationHistoryEntry { Name = "second", UpFile = "2.up.sql", DownFile = "2.down.sql" });
        await MigrationHistory.AppendAsync(_root, new MigrationHistoryEntry { Name = "third", UpFile = "3.up.sql", DownFile = "3.down.sql" });

        var history = await MigrationHistory.ReadAsync(Path.Combine(_root, MigrationHistory.DefaultFileName));

        Assert.Equal(3, history.Migrations.Count);
        Assert.Equal("first", history.Migrations[0].Name);
        Assert.Equal("second", history.Migrations[1].Name);
        Assert.Equal("third", history.Migrations[2].Name);
    }

    [Fact]
    public async Task Append_RecordsDestructiveFlag()
    {
        await MigrationHistory.AppendAsync(_root, new MigrationHistoryEntry
        {
            Name = "drop_table",
            UpFile = "drop.up.sql",
            DownFile = "drop.down.sql",
            Destructive = true
        });

        var history = await MigrationHistory.ReadAsync(Path.Combine(_root, MigrationHistory.DefaultFileName));

        Assert.True(Assert.Single(history.Migrations).Destructive);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
