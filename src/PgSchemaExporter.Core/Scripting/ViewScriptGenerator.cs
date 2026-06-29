using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class ViewScriptGenerator : ISqlScriptGenerator<DbView>
{
    public string Generate(DbView model)
    {
        var sb = new StringBuilder();
        var keyword = model.IsMaterialized ? "CREATE MATERIALIZED VIEW" : "CREATE OR REPLACE VIEW";
        sb.AppendLine($"{keyword} {SqlIdentifier.Qualified(model.Schema, model.Name)} AS");
        sb.AppendLine(model.Definition.Trim().TrimEnd(';') + ";");
        return sb.ToString();
    }
}
