using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class TableScriptGenerator : ISqlScriptGenerator<DbTable>
{
    public string Generate(DbTable table)
    {
        var sb = new StringBuilder();

        // PostgreSQL allows only one persistence modifier; TEMPORARY and UNLOGGED are mutually exclusive.
        var modifier = table.IsTemporary ? "TEMPORARY " : table.IsUnlogged ? "UNLOGGED " : "";

        sb.AppendLine($"CREATE {modifier}TABLE IF NOT EXISTS {SqlIdentifier.Qualified(table.Schema, table.Name)} (");

        var columns = table.Columns;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            sb.Append("    ");
            sb.Append(SqlIdentifier.Quote(column.Name));
            sb.Append(' ');
            sb.Append(BuildDataType(column));

            if (!column.IsNullable)
                sb.Append(" NOT NULL");

            if (column.IsIdentity)
            {
                sb.Append(" GENERATED");
                if (!string.IsNullOrWhiteSpace(column.IdentityGeneration))
                    sb.Append(' ').Append(column.IdentityGeneration);
                sb.Append(" AS IDENTITY");
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            {
                sb.Append(" DEFAULT ");
                sb.Append(column.DefaultValue);
            }

            if (!string.IsNullOrWhiteSpace(column.Collation))
                sb.Append(" COLLATE ").Append(column.Collation);

            if (i < columns.Count - 1)
                sb.Append(',');

            sb.AppendLine();
        }

        sb.Append(')');

        if (!string.IsNullOrWhiteSpace(table.InheritsFrom))
            sb.Append($" INHERITS ({QualifyName(table.InheritsFrom, table.Schema)})");

        if (!string.IsNullOrWhiteSpace(table.PartitionKey))
            sb.Append($" PARTITION BY {table.PartitionKey}");

        if (!string.IsNullOrWhiteSpace(table.Tablespace))
            sb.Append($" TABLESPACE {SqlIdentifier.Quote(table.Tablespace)}");

        sb.AppendLine(";");

        // A child partition requires its bound (FOR VALUES ...), which is not captured in the model.
        // Emit an explicit, valid note instead of generating invalid DDL.
        if (!string.IsNullOrWhiteSpace(table.PartitionOf))
            sb.AppendLine($"-- PARTITION OF {QualifyName(table.PartitionOf, table.Schema)} (attach manually; partition bounds are not exported)");

        return sb.ToString();
    }

    private static string QualifyName(string qualifiedName, string defaultSchema)
    {
        var parts = qualifiedName.Split('.', 2);
        return parts.Length == 2
            ? SqlIdentifier.Qualified(parts[0], parts[1])
            : SqlIdentifier.Qualified(defaultSchema, qualifiedName);
    }

    private static string BuildDataType(DbColumn column)
    {
        if ((column.DataType is "character varying" or "character") && column.CharacterMaximumLength.HasValue)
            return $"{column.DataType}({column.CharacterMaximumLength})";

        if ((column.DataType is "numeric" or "decimal") && column.NumericPrecision.HasValue)
        {
            if (column.NumericScale.HasValue)
                return $"{column.DataType}({column.NumericPrecision},{column.NumericScale})";

            return $"{column.DataType}({column.NumericPrecision})";
        }

        return column.DataType;
    }
}
