namespace PgSchemaExporter.Core.Models;

public sealed class DatabaseModel
{
    public IReadOnlyList<DbSchema> Schemas { get; set; } = [];
    public IReadOnlyList<DbExtension> Extensions { get; set; } = [];
    public IReadOnlyList<DbType> Types { get; set; } = [];
    public IReadOnlyList<DbSequence> Sequences { get; set; } = [];
    public IReadOnlyList<DbTable> Tables { get; set; } = [];
    public IReadOnlyList<DbConstraint> Constraints { get; set; } = [];
    public IReadOnlyList<DbIndex> Indexes { get; set; } = [];
    public IReadOnlyList<DbView> Views { get; set; } = [];
    public IReadOnlyList<DbFunction> Functions { get; set; } = [];
}
