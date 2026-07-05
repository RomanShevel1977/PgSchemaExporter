namespace PgSchemaExporter.Core.Models;

public sealed class DbOperator
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string LeftType { get; set; } = "";
    public string RightType { get; set; } = "";
    public string ResultType { get; set; } = "";
    public string Definition { get; set; } = "";
}
