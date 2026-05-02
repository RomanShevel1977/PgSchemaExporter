using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class ViewScriptGenerator : ISqlScriptGenerator<DbView>
{
    public string Generate(DbView model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE OR REPLACE VIEW {SqlIdentifier.Qualified(model.Schema, model.Name)} AS");
        sb.AppendLine(model.Definition.Trim().TrimEnd(';') + ";");
        return sb.ToString();
    }
}
