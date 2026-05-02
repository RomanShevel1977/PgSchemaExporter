using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class FunctionScriptGenerator : ISqlScriptGenerator<DbFunction>
{
    public string Generate(DbFunction model)
    {
        return model.Definition.TrimEnd() + Environment.NewLine;
    }
}
