using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class AggregateScriptGenerator : ISqlScriptGenerator<DbAggregate>
{
    public string Generate(DbAggregate item)
    {
        return item.Definition;
    }
}
