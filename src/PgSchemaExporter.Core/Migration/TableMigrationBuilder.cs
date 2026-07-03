using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Produces column-level <c>ALTER TABLE</c> statements for a table whose definition
/// changed between two exported schemas. This is the semantic core of migration
/// generation: instead of dropping and recreating the table (which loses data), it
/// emits targeted ADD/DROP/ALTER COLUMN statements with matching rollback statements.
/// </summary>
public static class TableMigrationBuilder
{
    public sealed class Result
    {
        public List<MigrationStatement> Up { get; } = [];
        public List<MigrationStatement> Down { get; } = [];
    }

    public static Result Build(ParsedTable from, ParsedTable to)
    {
        var result = new Result();
        var table = to.QualifiedName;

        var oldColumns = from.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var newColumns = to.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

        // Added columns.
        foreach (var column in to.Columns)
        {
            if (oldColumns.ContainsKey(column.Name))
                continue;

            var quoted = SqlIdentifier.Quote(column.Name);
            result.Up.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"ALTER TABLE {table} ADD COLUMN {quoted} {column.Definition};"));
            result.Down.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"ALTER TABLE {table} DROP COLUMN IF EXISTS {quoted};",
                isDestructive: true));
        }

        // Removed columns.
        foreach (var column in from.Columns)
        {
            if (newColumns.ContainsKey(column.Name))
                continue;

            var quoted = SqlIdentifier.Quote(column.Name);
            result.Up.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"ALTER TABLE {table} DROP COLUMN IF EXISTS {quoted};",
                isDestructive: true));
            result.Down.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"ALTER TABLE {table} ADD COLUMN {quoted} {column.Definition};"));
        }

        // Altered columns.
        foreach (var newColumn in to.Columns)
        {
            if (!oldColumns.TryGetValue(newColumn.Name, out var oldColumn))
                continue;

            BuildColumnAlter(result, table, oldColumn, newColumn);
        }

        return result;
    }

    private static void BuildColumnAlter(Result result, string table, ParsedColumn from, ParsedColumn to)
    {
        var quoted = SqlIdentifier.Quote(to.Name);

        // Data type.
        if (!string.Equals(NormalizeType(from.DataType), NormalizeType(to.DataType), StringComparison.OrdinalIgnoreCase))
        {
            result.Up.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"ALTER TABLE {table} ALTER COLUMN {quoted} TYPE {to.DataType};",
                isDestructive: true,
                comment: $"Column type change may require a USING clause: {to.Name}"));
            result.Down.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"ALTER TABLE {table} ALTER COLUMN {quoted} TYPE {from.DataType};",
                isDestructive: true));
        }

        // Nullability.
        if (from.NotNull != to.NotNull)
        {
            if (to.NotNull)
            {
                result.Up.Add(new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quoted} SET NOT NULL;"));
                result.Down.Add(new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quoted} DROP NOT NULL;"));
            }
            else
            {
                result.Up.Add(new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quoted} DROP NOT NULL;"));
                result.Down.Add(new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quoted} SET NOT NULL;"));
            }
        }

        // Default.
        if (!string.Equals(from.Default?.Trim(), to.Default?.Trim(), StringComparison.Ordinal))
        {
            result.Up.Add(BuildDefaultChange(table, quoted, to.Default));
            result.Down.Add(BuildDefaultChange(table, quoted, from.Default));
        }

        // Identity / collation changes are reported but not auto-altered (rare, risky).
        if (!string.Equals(from.Identity?.Trim(), to.Identity?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            result.Up.Add(new MigrationStatement(
                MigrationObjectKind.Table,
                $"-- TODO: identity changed for {table}.{quoted}; review manually (old: {from.Identity ?? "none"}, new: {to.Identity ?? "none"}).",
                comment: "Identity change requires manual review"));
        }

        if (!string.Equals(from.Collation?.Trim(), to.Collation?.Trim(), StringComparison.Ordinal))
        {
            var newCollation = string.IsNullOrWhiteSpace(to.Collation) ? "\"default\"" : to.Collation;
            var oldCollation = string.IsNullOrWhiteSpace(from.Collation) ? "\"default\"" : from.Collation;
            result.Up.Add(new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quoted} TYPE {to.DataType} COLLATE {newCollation};"));
            result.Down.Add(new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quoted} TYPE {from.DataType} COLLATE {oldCollation};"));
        }
    }

    private static MigrationStatement BuildDefaultChange(string table, string quotedColumn, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quotedColumn} DROP DEFAULT;");

        return new MigrationStatement(MigrationObjectKind.Table, $"ALTER TABLE {table} ALTER COLUMN {quotedColumn} SET DEFAULT {value};");
    }

    private static string NormalizeType(string type)
        => string.Join(' ', type.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
