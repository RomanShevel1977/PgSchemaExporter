namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// A single SQL statement that is part of a migration, tagged with the object kind
/// it targets (for ordering) and whether it may destroy data.
/// </summary>
public sealed class MigrationStatement
{
    public MigrationStatement(MigrationObjectKind kind, string sql, bool isDestructive = false, string? comment = null)
    {
        Kind = kind;
        Sql = sql;
        IsDestructive = isDestructive;
        Comment = comment;
    }

    public MigrationObjectKind Kind { get; }

    public string Sql { get; }

    /// <summary>True for statements that can lose data, e.g. DROP TABLE/COLUMN or type changes.</summary>
    public bool IsDestructive { get; }

    /// <summary>Optional explanatory comment emitted above the statement.</summary>
    public string? Comment { get; }
}
