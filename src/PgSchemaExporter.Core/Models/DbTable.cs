namespace PgSchemaExporter.Core.Models;

public sealed class DbTable
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public List<DbColumn> Columns { get; set; } = [];
}
