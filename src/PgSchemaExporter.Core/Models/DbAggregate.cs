namespace PgSchemaExporter.Core.Models;

public sealed class DbAggregate
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string InputType { get; set; } = "";
    public string? StateType { get; set; }
    public string? FinalizeFunc { get; set; }
    public string Definition { get; set; } = "";
}
