namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// The kind of database object a migration statement targets. Derived from the
/// top-level folder of an exported schema file (e.g. <c>tables/</c>, <c>views/</c>).
/// The numeric value defines deployment ordering for the generated up migration;
/// the down migration uses the reverse order.
/// </summary>
public enum MigrationObjectKind
{
    Schema = 0,
    Extension = 1,
    Type = 2,
    Sequence = 3,
    Domain = 4,
    Table = 5,
    ForeignTable = 6,
    Constraint = 7,
    Index = 8,
    View = 9,
    Function = 10,
    Trigger = 11,
    Policy = 12,
    Comment = 13,
    Grant = 14,
    Unknown = 99
}

public static class MigrationObjectKinds
{
    /// <summary>
    /// Maps the leading folder of a relative path (e.g. <c>tables/public.users.sql</c>)
    /// to its <see cref="MigrationObjectKind"/>.
    /// </summary>
    public static MigrationObjectKind FromRelativePath(string relativePath)
    {
        var folder = relativePath.Replace('\\', '/').Split('/', 2)[0].ToLowerInvariant();

        return folder switch
        {
            "schemas" => MigrationObjectKind.Schema,
            "extensions" => MigrationObjectKind.Extension,
            "types" => MigrationObjectKind.Type,
            "sequences" => MigrationObjectKind.Sequence,
            "domains" => MigrationObjectKind.Domain,
            "tables" => MigrationObjectKind.Table,
            "foreign_tables" => MigrationObjectKind.ForeignTable,
            "constraints" => MigrationObjectKind.Constraint,
            "indexes" => MigrationObjectKind.Index,
            "views" => MigrationObjectKind.View,
            "functions" => MigrationObjectKind.Function,
            "triggers" => MigrationObjectKind.Trigger,
            "policies" => MigrationObjectKind.Policy,
            "comments" => MigrationObjectKind.Comment,
            "grants" => MigrationObjectKind.Grant,
            _ => MigrationObjectKind.Unknown
        };
    }
}
