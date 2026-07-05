using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class PublicationScriptGenerator : ISqlScriptGenerator<DbPublication>
{
    public string Generate(DbPublication item)
    {
        return item.Definition;
    }
}
