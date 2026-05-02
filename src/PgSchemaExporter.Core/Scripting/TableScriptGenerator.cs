using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class TableScriptGenerator : ISqlScriptGenerator<DbTable>
{
    public string Generate(DbTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS {SqlIdentifier.Qualified(table.Schema, table.Name)} (");

        var columns = table.Columns.OrderBy(x => x.OrdinalPosition).ToList();

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            sb.Append("    ");
            sb.Append(SqlIdentifier.Quote(column.Name));
            sb.Append(' ');
            sb.Append(BuildDataType(column));

            if (!column.IsNullable)
                sb.Append(" NOT NULL");

            if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            {
                sb.Append(" DEFAULT ");
                sb.Append(column.DefaultValue);
            }

            if (i < columns.Count - 1)
                sb.Append(',');

            sb.AppendLine();
        }

        sb.AppendLine(");");
        return sb.ToString();
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
