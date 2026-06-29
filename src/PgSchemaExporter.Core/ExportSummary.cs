namespace PgSchemaExporter.Core;

public sealed class ExportSummary
{
    public bool DryRun { get; init; }
    public string OutputDirectory { get; init; } = "";
    public IReadOnlyList<(string ObjectKind, int Count)> Counts { get; init; } = [];
    public int TotalObjects => Counts.Sum(x => x.Count);
}
