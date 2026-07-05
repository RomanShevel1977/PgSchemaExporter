using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class EventTriggerScriptGenerator : ISqlScriptGenerator<DbEventTrigger>
{
    public string Generate(DbEventTrigger item)
    {
        return item.Definition;
    }
}
