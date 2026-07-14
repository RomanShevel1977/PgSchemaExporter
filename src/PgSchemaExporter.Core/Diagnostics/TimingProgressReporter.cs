using System.Diagnostics;
using System.Text;

namespace PgSchemaExporter.Core.Diagnostics;

/// <summary>
/// A decorator that measures how long each reported phase takes. It forwards every
/// call to an inner reporter and records the elapsed time between successive
/// <see cref="Start"/>/<see cref="Step"/>/<see cref="Complete"/> boundaries, so a
/// <c>--profile</c> summary can show where time was spent.
/// </summary>
public sealed class TimingProgressReporter : IProgressReporter
{
    private readonly IProgressReporter _inner;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly List<PhaseTiming> _phases = [];

    private string? _pendingLabel;
    private TimeSpan _pendingStart;

    public TimingProgressReporter(IProgressReporter inner)
    {
        _inner = inner ?? NullProgressReporter.Instance;
    }

    public IReadOnlyList<PhaseTiming> Phases => _phases;

    public TimeSpan Total => _stopwatch.Elapsed;

    public void Start(string operation, int? totalSteps = null)
    {
        _inner.Start(operation, totalSteps);
        Boundary(operation);
    }

    public void Step(string message)
    {
        _inner.Step(message);
        Boundary(message);
    }

    public void Complete(string message)
    {
        _inner.Complete(message);
        Boundary(null);
    }

    /// <summary>Closes the current phase (if any) and opens a new one labelled <paramref name="label"/>.</summary>
    private void Boundary(string? label)
    {
        var now = _stopwatch.Elapsed;

        if (_pendingLabel is not null)
            _phases.Add(new PhaseTiming(_pendingLabel, now - _pendingStart));

        _pendingLabel = label;
        _pendingStart = now;
    }

    /// <summary>
    /// Finalizes any open phase and returns a human-readable timing summary.
    /// Safe to call more than once.
    /// </summary>
    public string BuildSummary()
    {
        Boundary(null);

        var sb = new StringBuilder();
        sb.AppendLine("Performance profile:");

        foreach (var phase in _phases)
            sb.AppendLine($"  {Format(phase.Elapsed),10}  {phase.Label}");

        sb.AppendLine($"  {Format(Total),10}  total");
        return sb.ToString().TrimEnd();
    }

    private static string Format(TimeSpan elapsed)
        => elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:0.00}s"
            : $"{elapsed.TotalMilliseconds:0}ms";
}

public readonly record struct PhaseTiming(string Label, TimeSpan Elapsed);
