namespace PgSchemaExporter.Core.Models;

public sealed class DbSequence
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public long StartValue { get; set; }
    public long MinimumValue { get; set; }
    public long MaximumValue { get; set; }
    public long Increment { get; set; }
    public bool Cycle { get; set; }
}
