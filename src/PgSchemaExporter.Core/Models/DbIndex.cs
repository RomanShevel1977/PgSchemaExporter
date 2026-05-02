namespace PgSchemaExporter.Core.Models;

public sealed class DbIndex
{
    public string Schema { get; set; } = "";
    public string TableName { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool IsUnique { get; set; }
    public string Definition { get; set; } = "";
}
