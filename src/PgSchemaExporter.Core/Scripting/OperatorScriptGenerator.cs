using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class OperatorScriptGenerator : ISqlScriptGenerator<DbOperator>
{
    public string Generate(DbOperator item)
    {
        return item.Definition;
    }
}
