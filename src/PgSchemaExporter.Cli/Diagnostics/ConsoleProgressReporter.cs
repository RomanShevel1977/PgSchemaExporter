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
    private int? _total;

    public ConsoleProgressReporter(Verbosity verbosity)
    {
        _verbosity = verbosity;
    }

    public void Start(string operation, int? totalSteps = null)
    {
        _completed = 0;
        _total = totalSteps;

        if (_verbosity >= Verbosity.Normal)
            Console.Error.WriteLine($"==> {operation}...");
    }

    public void Step(string message)
    {
        _completed++;

        if (_verbosity < Verbosity.Verbose)
            return;

        var counter = _total is > 0 ? $"[{_completed}/{_total}] " : string.Empty;
        Console.Error.WriteLine($"    {counter}{message}");
    }

    public void Complete(string message)
    {
        if (_verbosity >= Verbosity.Normal)
            Console.Error.WriteLine($"<== {message}");
    }
}
