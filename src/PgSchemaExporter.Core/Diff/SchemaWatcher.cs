using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Diff;

/// <summary>
/// Watches two exported schema directories and re-runs the directory diff whenever
/// any <c>.sql</c> file changes under either tree. File-system events are debounced so
/// that a burst of writes results in a single recomputation.
/// </summary>
public sealed class SchemaWatcher
{
    private readonly SchemaDiffer _differ;
    private readonly int _debounceMilliseconds;

    public SchemaWatcher(SchemaDiffer? differ = null, int debounceMilliseconds = 400)
    {
        _differ = differ ?? new SchemaDiffer();
        _debounceMilliseconds = debounceMilliseconds;
    }

    public async Task WatchAsync(
        SchemaDiffOptions options,
        Func<SchemaDiffResult, Task> onChangeAsync,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(options.LeftConnectionString) ||
            !string.IsNullOrWhiteSpace(options.RightConnectionString))
            throw new InvalidOperationException("Watch mode supports directory comparisons only, not live databases.");

        options.EnsureValid();

        // Emit the initial diff before watching for changes.
        await onChangeAsync(_differ.Diff(options));

        using var trigger = new SemaphoreSlim(0, 1);

        void OnChanged(object? sender, FileSystemEventArgs e)
        {
            // Release only if not already pending to coalesce bursts of events.
            try { trigger.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }

        using var leftWatcher = CreateWatcher(options.LeftDirectory, OnChanged);
        using var rightWatcher = CreateWatcher(options.RightDirectory, OnChanged);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await trigger.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Debounce: wait for the file-system to settle before recomputing.
            await Task.Delay(_debounceMilliseconds, cancellationToken);

            // Drain any additional signal accumulated during the debounce window.
            while (trigger.CurrentCount > 0)
            {
                try
                {
                    await trigger.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            await onChangeAsync(_differ.Diff(options));
        }
    }

    private static FileSystemWatcher CreateWatcher(string directory, FileSystemEventHandler handler)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            Filter = "*.sql",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (s, e) => handler(s, e);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}
