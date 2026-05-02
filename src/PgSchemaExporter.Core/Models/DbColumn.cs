namespace PgSchemaExporter.Core.Models;

public sealed class DbColumn
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
    public string? DefaultValue { get; set; }
    public int OrdinalPosition { get; set; }
    public int? CharacterMaximumLength { get; set; }
    public int? NumericPrecision { get; set; }
    public int? NumericScale { get; set; }
}
