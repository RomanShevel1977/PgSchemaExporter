using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaWatcherTests : IDisposable
{
    private readonly string _root;
    private readonly string _left;
    private readonly string _right;

    public SchemaWatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-watch-" + Guid.NewGuid().ToString("n"));
        _left = Path.Combine(_root, "left");
        _right = Path.Combine(_root, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    [Fact]
    public async Task WatchAsync_EmitsInitialDiffImmediately()
    {
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);");

        var watcher = new SchemaWatcher(debounceMilliseconds: 50);
        using var cts = new CancellationTokenSource();

        SchemaDiffResult? initial = null;
        var received = new TaskCompletionSource();

        var watchTask = watcher.WatchAsync(
            new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right },
            result =>
            {
                initial ??= result;
                received.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await SafeAwait(watchTask);

        Assert.NotNull(initial);
        Assert.Single(initial!.Added);
        Assert.Equal("tables/public.users.sql", initial.Added[0]);
    }

    [Fact]
    public async Task WatchAsync_RecomputesOnFileChange()
    {
        var watcher = new SchemaWatcher(debounceMilliseconds: 50);
        using var cts = new CancellationTokenSource();

        var callCount = 0;
        var secondCall = new TaskCompletionSource();

        var watchTask = watcher.WatchAsync(
            new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right },
            _ =>
            {
                if (Interlocked.Increment(ref callCount) >= 2)
                    secondCall.TrySetResult();
                return Task.CompletedTask;
            },
            cts.Token);

        // Give the watcher a moment to emit the initial diff and start watching.
        await Task.Delay(200, cts.Token);
        WriteFile(_right, "tables/public.orders.sql", "CREATE TABLE orders (id int);");

        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await SafeAwait(watchTask);

        Assert.True(callCount >= 2);
    }

    [Fact]
    public async Task WatchAsync_RejectsLiveDatabases()
    {
        var watcher = new SchemaWatcher();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            watcher.WatchAsync(
                new SchemaDiffOptions { LeftConnectionString = "Host=localhost", RightDirectory = _right },
                _ => Task.CompletedTask));
    }

    private static async Task SafeAwait(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
