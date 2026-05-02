namespace PgSchemaExporter.Core.Models;

public sealed class SqlDumpObject
{
    public SqlObjectType Type { get; set; }
    public string Schema { get; set; } = "public";
    public string Name { get; set; } = "unknown";
    public string? ParentName { get; set; }
    public string Statement { get; set; } = "";
    public int Order { get; set; }
}
