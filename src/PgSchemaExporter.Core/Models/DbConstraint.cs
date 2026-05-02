namespace PgSchemaExporter.Core.Models;

public sealed class DbConstraint
{
    public string Schema { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Definition { get; set; } = "";
}
