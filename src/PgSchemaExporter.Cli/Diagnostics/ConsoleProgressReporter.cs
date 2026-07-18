using PgSchemaExporter.Core.Diagnostics;

namespace PgSchemaExporter.Cli.Diagnostics;

/// <summary>
/// Renders progress to the console, honoring the selected <see cref="Verbosity"/>.
/// Quiet suppresses all progress; Normal shows start/complete milestones; Verbose
/// shows every step with a running counter.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter
{
    private readonly Verbosity _verbosity;
    private int _completed;
    private int _total = -1; // -1 means "no total provided"

    public ConsoleProgressReporter(Verbosity verbosity)
    {
        _verbosity = verbosity;
    }

    public void Start(string operation, int? totalSteps = null)
    {
        _completed = 0;
        Volatile.Write(ref _total, totalSteps ?? -1);

        if (_verbosity >= Verbosity.Normal)
            Console.Error.WriteLine($"==> {operation}...");
    }

    public void Step(string message)
    {
        var completed = Interlocked.Increment(ref _completed);

        if (_verbosity < Verbosity.Verbose)
            return;

        var total = Volatile.Read(ref _total);
        var counter = total > 0 ? $"[{completed}/{total}] " : string.Empty;
        Console.Error.WriteLine($"    {counter}{message}");
    }

    public void Complete(string message)
    {
        if (_verbosity >= Verbosity.Normal)
            Console.Error.WriteLine($"<== {message}");
    }
}
