namespace PgSchemaExporter.Core.Models;

public sealed class DbTable
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public List<DbColumn> Columns { get; set; } = [];
    public string? Tablespace { get; set; }
    public string? InheritsFrom { get; set; }
    public string? PartitionOf { get; set; }
    public string? PartitionKey { get; set; }
    public bool IsUnlogged { get; set; }
    public bool IsTemporary { get; set; }
}
