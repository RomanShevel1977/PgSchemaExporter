using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class SubscriptionScriptGenerator : ISqlScriptGenerator<DbSubscription>
{
    public string Generate(DbSubscription item)
    {
        return item.Definition;
    }
}
