namespace PgSchemaExporter.Core.Diff;

public sealed class SchemaDiffResult
{
    public IReadOnlyList<string> Added { get; init; } = [];
    public IReadOnlyList<string> Removed { get; init; } = [];
    public IReadOnlyList<string> Changed { get; init; } = [];
    public IReadOnlyList<string> Unchanged { get; init; } = [];

    public bool HasDifferences => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;
}
