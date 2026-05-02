using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class IndexScriptGenerator : ISqlScriptGenerator<DbIndex>
{
    public string Generate(DbIndex model)
    {
        var sql = model.Definition.Trim().TrimEnd(';');

        if (!sql.Contains(" IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
            && sql.StartsWith("CREATE INDEX ", StringComparison.OrdinalIgnoreCase))
        {
            sql = "CREATE INDEX IF NOT EXISTS " + sql["CREATE INDEX ".Length..];
        }
        else if (!sql.Contains(" IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
                 && sql.StartsWith("CREATE UNIQUE INDEX ", StringComparison.OrdinalIgnoreCase))
        {
            sql = "CREATE UNIQUE INDEX IF NOT EXISTS " + sql["CREATE UNIQUE INDEX ".Length..];
        }

        return sql + ";" + Environment.NewLine;
    }
}
