using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class ForeignTableScriptGenerator : ISqlScriptGenerator<DbForeignTable>
{
    public string Generate(DbForeignTable model)
    {
        var sb = new StringBuilder();
        sb.AppendLine(model.Definition.Trim().TrimEnd(';') + ";");
        return sb.ToString();
    }
}
