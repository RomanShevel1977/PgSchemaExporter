namespace PgSchemaExporter.Core.Models;

public sealed class DbDomain
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string Definition { get; set; } = "";
}
