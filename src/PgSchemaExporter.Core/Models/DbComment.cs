namespace PgSchemaExporter.Core.Models;

public sealed class DbComment
{
    public string Schema { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string? SubObject { get; set; }
    public string Definition { get; set; } = "";
}
