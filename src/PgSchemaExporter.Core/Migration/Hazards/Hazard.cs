namespace PgSchemaExporter.Core.Migration.Hazards;

/// <summary>Severity of a migration hazard.</summary>
public enum HazardSeverity
{
    Low,
    Medium,
    High
}

/// <summary>Category of a migration hazard, used for filtering and reporting.</summary>
public enum HazardCategory
{
    /// <summary>DROP TABLE / DROP FOREIGN TABLE — irreversible data loss.</summary>
    TableDrop,

    /// <summary>DROP COLUMN — irreversible data loss for that column.</summary>
    ColumnDrop,

    /// <summary>ALTER COLUMN ... TYPE — table rewrite and ACCESS EXCLUSIVE lock.</summary>
    TypeChange,

    /// <summary>SET NOT NULL — full table scan while holding a lock.</summary>
    NotNull,

    /// <summary>Non-concurrent CREATE INDEX — blocks writes for the duration of the build.</summary>
    IndexBuild,

    /// <summary>DROP of another object kind (type, sequence, domain, etc.).</summary>
    ObjectDrop,

    /// <summary>Other destructive statement flagged by the generator.</summary>
    DataLoss
}

/// <summary>
/// A single detected hazard within a generated migration: what it is, how severe
/// it is, and the statement that triggered it.
/// </summary>
public sealed class Hazard
{
    public required HazardCategory Category { get; init; }
    public required HazardSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required string Statement { get; init; }
}
