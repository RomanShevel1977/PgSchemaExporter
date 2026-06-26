namespace PgSchemaExporter.Core.Models;

public sealed class DbPolicy
{
    public string Schema { get; set; } = "";
    public string TableSchema { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Definition { get; set; } = "";
}
