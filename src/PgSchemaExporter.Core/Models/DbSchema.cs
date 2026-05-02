namespace PgSchemaExporter.Core.Models;

public sealed class DbSchema
{
    public string Name { get; set; } = "";
    public string? Owner { get; set; }
}
