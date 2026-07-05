namespace PgSchemaExporter.Core.Models;

public sealed class DbCast
{
    public string SourceType { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string Definition { get; set; } = "";
}
