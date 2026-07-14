using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaWatcherIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public SchemaWatcherIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-watcher-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task WatchAsync_FileChange_EmitsUpdatedDiff()
    {
        var leftDir = Path.Combine(_tempRoot, "left");
        var rightDir = Path.Combine(_tempRoot, "right");

        Directory.CreateDirectory(leftDir);
        Directory.CreateDirectory(rightDir);
        Directory.CreateDirectory(Path.Combine(leftDir, "tables"));
        Directory.CreateDirectory(Path.Combine(rightDir, "tables"));

        await File.WriteAllTextAsync(Path.Combine(leftDir, "tables", "common.sql"), "CREATE TABLE t (id int);");
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "common.sql"), "CREATE TABLE t (id int);");

        var watcher = new SchemaWatcher(debounceMilliseconds: 50);
        using var cts = new CancellationTokenSource();

        var results = new List<SchemaDiffResult>();
        var tcs = new TaskCompletionSource();

        var watchTask = watcher.WatchAsync(
            new SchemaDiffOptions { LeftDirectory = leftDir, RightDirectory = rightDir },
            result =>
            {
                results.Add(result);
                if (results.Count == 2)
                    tcs.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        // Wait for the initial diff to be emitted.
        await Task.Delay(200);

        // Trigger a file-system change by adding a new file on the right side.
        await File.WriteAllTextAsync(Path.Combine(rightDir, "tables", "added.sql"), "CREATE TABLE added (id int);");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await watchTask; } catch (OperationCanceledException) { }

        Assert.True(results.Count >= 2);
        Assert.False(results[0].HasDifferences);
        Assert.True(results[^1].HasDifferences);
        Assert.Contains(results[^1].Added, x => x.Contains("added.sql"));
    }

    [Fact]
    public async Task WatchAsync_LiveDatabase_ThrowsInvalidOperationException()
    {
        var watcher = new SchemaWatcher();
        var options = new SchemaDiffOptions
        {
            LeftDirectory = _tempRoot,
            RightConnectionString = "Host=localhost;Database=db"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            watcher.WatchAsync(options, _ => Task.CompletedTask, CancellationToken.None));
    }
}
