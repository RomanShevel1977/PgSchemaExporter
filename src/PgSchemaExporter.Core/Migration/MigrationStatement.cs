namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// A single SQL statement that is part of a migration, tagged with the object kind
/// it targets (for ordering) and whether it may destroy data.
/// </summary>
public sealed class MigrationStatement
{
    public MigrationStatement(
        MigrationObjectKind kind,
        string sql,
        bool isDestructive = false,
        string? comment = null,
        bool runsOutsideTransaction = false)
    {
        Kind = kind;
        Sql = sql;
        IsDestructive = isDestructive;
        Comment = comment;
        RunsOutsideTransaction = runsOutsideTransaction;
    }

    public MigrationObjectKind Kind { get; }

    public string Sql { get; }

    /// <summary>True for statements that can lose data, e.g. DROP TABLE/COLUMN or type changes.</summary>
    public bool IsDestructive { get; }

    /// <summary>Optional explanatory comment emitted above the statement.</summary>
    public string? Comment { get; }

    /// <summary>
    /// True for statements that cannot run inside a transaction block, e.g.
    /// <c>CREATE INDEX CONCURRENTLY</c>. These are rendered and executed outside
    /// the wrapping <c>BEGIN/COMMIT</c>.
    /// </summary>
    public bool RunsOutsideTransaction { get; }
}
