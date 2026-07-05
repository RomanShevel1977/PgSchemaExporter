using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class CastScriptGenerator : ISqlScriptGenerator<DbCast>
{
    public string Generate(DbCast item)
    {
        return item.Definition;
    }
}
