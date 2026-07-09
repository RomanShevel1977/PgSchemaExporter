namespace PgSchemaExporter.Core.Diff;

public sealed class SchemaDiffResult
{
    public IReadOnlyList<string> Added { get; init; } = [];
    public IReadOnlyList<string> Removed { get; init; } = [];
    public IReadOnlyList<string> Changed { get; init; } = [];
    public IReadOnlyList<string> Unchanged { get; init; } = [];

    /// <summary>Per-object-type change counts (grouped by the top-level export folder).</summary>
    public IReadOnlyList<DiffTypeStat> Statistics { get; init; } = [];

    /// <summary>Line-by-line changes for each changed file (populated when context is requested).</summary>
    public IReadOnlyList<FileDiff> FileDiffs { get; init; } = [];

    public bool HasDifferences => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;
}

/// <summary>Aggregated add/remove/change counts for a single object type.</summary>
public sealed class DiffTypeStat
{
    public string ObjectType { get; init; } = "";
    public int Added { get; init; }
    public int Removed { get; init; }
    public int Changed { get; init; }

    public int Total => Added + Removed + Changed;
}

/// <summary>Line-by-line diff for a single changed file.</summary>
public sealed class FileDiff
{
    public string Path { get; init; } = "";
    public IReadOnlyList<DiffLine> Lines { get; init; } = [];
}

public sealed class DiffLine
{
    public DiffLineKind Kind { get; init; }
    public string Text { get; init; } = "";
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed
}
