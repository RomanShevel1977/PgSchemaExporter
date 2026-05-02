using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class SchemaScriptGenerator : ISqlScriptGenerator<DbSchema>
{
    public string Generate(DbSchema model)
    {
        return $"CREATE SCHEMA IF NOT EXISTS {SqlIdentifier.Quote(model.Name)};{Environment.NewLine}";
    }
}
