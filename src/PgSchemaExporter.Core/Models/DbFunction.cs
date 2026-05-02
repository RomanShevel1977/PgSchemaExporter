namespace PgSchemaExporter.Core.Models;

public sealed class DbFunction
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string ArgumentsIdentity { get; set; } = "";
    public string Definition { get; set; } = "";
}
