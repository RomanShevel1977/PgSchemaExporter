namespace PgSchemaExporter.Core.Models;

public sealed class DbExtension
{
    public string Name { get; set; } = "";
    public string Schema { get; set; } = "";
    public string Version { get; set; } = "";
}
