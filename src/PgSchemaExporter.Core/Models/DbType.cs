namespace PgSchemaExporter.Core.Models;

public sealed class DbType
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public IReadOnlyList<string> EnumLabels { get; set; } = [];
}
