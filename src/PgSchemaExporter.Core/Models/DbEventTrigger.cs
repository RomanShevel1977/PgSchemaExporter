namespace PgSchemaExporter.Core.Models;

public sealed class DbEventTrigger
{
    public string Name { get; set; } = "";
    public string Event { get; set; } = "";
    public string? When { get; set; }
    public string Procedure { get; set; } = "";
    public string Definition { get; set; } = "";
}
