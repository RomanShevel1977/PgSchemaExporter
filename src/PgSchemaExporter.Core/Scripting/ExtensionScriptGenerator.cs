using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class ExtensionScriptGenerator : ISqlScriptGenerator<DbExtension>
{
    public string Generate(DbExtension model)
    {
        return $"CREATE EXTENSION IF NOT EXISTS {SqlIdentifier.Quote(model.Name)} WITH SCHEMA {SqlIdentifier.Quote(model.Schema)};{Environment.NewLine}";
    }
}
