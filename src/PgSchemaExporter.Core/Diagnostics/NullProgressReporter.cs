namespace PgSchemaExporter.Core.Diagnostics;

/// <summary>
/// A no-op <see cref="IProgressReporter"/> used as the default when no reporter is
/// supplied (for example, in tests or library consumers that don't need output).
/// </summary>
public sealed class NullProgressReporter : IProgressReporter
{
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter()
    {
    }

    public void Start(string operation, int? totalSteps = null)
    {
    }

    public void Step(string message)
    {
    }

    public void Complete(string message)
    {
    }
}
