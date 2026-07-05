namespace PgSchemaExporter.Core.Models;

public sealed class DbRule
{
    public string Schema { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Definition { get; set; } = "";
}
