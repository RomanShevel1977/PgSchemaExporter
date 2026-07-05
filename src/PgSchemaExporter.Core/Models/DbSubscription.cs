namespace PgSchemaExporter.Core.Models;

public sealed class DbSubscription
{
    public string Name { get; set; } = "";
    public string Publication { get; set; } = "";
    public string? ConnectionString { get; set; }
    public string Definition { get; set; } = "";
}
