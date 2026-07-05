namespace PgSchemaExporter.Core.Models;

public sealed class DbPublication
{
    public string Name { get; set; } = "";
    public string? Tables { get; set; }
    public string Definition { get; set; } = "";
}
