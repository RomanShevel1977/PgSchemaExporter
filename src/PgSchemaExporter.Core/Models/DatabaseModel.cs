namespace PgSchemaExporter.Core.Models;

public sealed class DatabaseModel
{
    public IReadOnlyList<DbSchema> Schemas { get; set; } = [];
    public IReadOnlyList<DbExtension> Extensions { get; set; } = [];
    public IReadOnlyList<DbType> Types { get; set; } = [];
    public IReadOnlyList<DbSequence> Sequences { get; set; } = [];
    public IReadOnlyList<DbDomain> Domains { get; set; } = [];
    public IReadOnlyList<DbForeignTable> ForeignTables { get; set; } = [];
    public IReadOnlyList<DbTable> Tables { get; set; } = [];
    public IReadOnlyList<DbConstraint> Constraints { get; set; } = [];
    public IReadOnlyList<DbIndex> Indexes { get; set; } = [];
    public IReadOnlyList<DbView> Views { get; set; } = [];
    public IReadOnlyList<DbTrigger> Triggers { get; set; } = [];
    public IReadOnlyList<DbPolicy> Policies { get; set; } = [];
    public IReadOnlyList<DbComment> Comments { get; set; } = [];
    public IReadOnlyList<DbGrant> Grants { get; set; } = [];
    public IReadOnlyList<DbFunction> Functions { get; set; } = [];
}
