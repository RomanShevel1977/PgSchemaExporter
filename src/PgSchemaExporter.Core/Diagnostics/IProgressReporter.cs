namespace PgSchemaExporter.Core.Diagnostics;

/// <summary>
/// Reports coarse-grained progress for long-running operations such as live
/// exports and database diffs. Implementations decide how (or whether) to render
/// progress; core logic depends only on this abstraction.
/// </summary>
public interface IProgressReporter
{
    /// <summary>Signals the start of a named operation, optionally with a known number of steps.</summary>
    void Start(string operation, int? totalSteps = null);

    /// <summary>Reports that a single step within the current operation has progressed.</summary>
    void Step(string message);

    /// <summary>Signals that the current operation finished successfully.</summary>
    void Complete(string message);
}
