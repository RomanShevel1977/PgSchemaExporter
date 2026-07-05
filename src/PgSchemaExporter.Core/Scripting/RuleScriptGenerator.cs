using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class RuleScriptGenerator : ISqlScriptGenerator<DbRule>
{
    public string Generate(DbRule item)
    {
        return item.Definition;
    }
}
