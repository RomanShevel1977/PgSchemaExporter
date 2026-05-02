using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class TypeScriptGenerator : ISqlScriptGenerator<DbType>
{
    public string Generate(DbType model)
    {
        if (model.Kind != "e")
            return $"-- Unsupported type kind: {model.Kind}{Environment.NewLine}";

        var labels = string.Join(", ", model.EnumLabels.Select(SqlLiteral.String));

        var sb = new StringBuilder();
        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF NOT EXISTS (");
        sb.AppendLine("        SELECT 1");
        sb.AppendLine("        FROM pg_type t");
        sb.AppendLine("        JOIN pg_namespace n ON n.oid = t.typnamespace");
        sb.AppendLine($"        WHERE n.nspname = {SqlLiteral.String(model.Schema)}");
        sb.AppendLine($"          AND t.typname = {SqlLiteral.String(model.Name)}");
        sb.AppendLine("    ) THEN");
        sb.AppendLine($"        CREATE TYPE {SqlIdentifier.Qualified(model.Schema, model.Name)} AS ENUM ({labels});");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END");
        sb.AppendLine("$$;");
        return sb.ToString();
    }
}
