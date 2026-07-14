namespace PgSchemaExporter.Core.Diagramming;

/// <summary>
/// A minimal entity-relationship model of a schema: tables, their columns, and
/// the foreign-key relationships between them. Built either from a live database
/// model or from an exported schema directory, and rendered to Mermaid or DOT.
/// </summary>
public sealed class ErModel
{
    public IReadOnlyList<ErTable> Tables { get; init; } = [];
    public IReadOnlyList<ErRelationship> Relationships { get; init; } = [];

    public bool IsEmpty => Tables.Count == 0;
}

public sealed class ErTable
{
    public string Schema { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<ErColumn> Columns { get; init; } = [];

    /// <summary>The schema-qualified table name, e.g. <c>public.users</c>.</summary>
    public string QualifiedName =>
        string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

public sealed class ErColumn
{
    public string Name { get; init; } = "";
    public string DataType { get; init; } = "";
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; init; }
    public bool IsUnique { get; init; }
}

/// <summary>A foreign-key relationship from one table to another.</summary>
public sealed class ErRelationship
{
    public string Name { get; init; } = "";
    public string FromTable { get; init; } = "";
    public IReadOnlyList<string> FromColumns { get; init; } = [];
    public string ToTable { get; init; } = "";
    public IReadOnlyList<string> ToColumns { get; init; } = [];

    /// <summary>
    /// True when every referencing column is NOT NULL, which makes the child side
    /// mandatory (rendered as a "one" rather than "zero-or-one" cardinality).
    /// </summary>
    public bool IsMandatory { get; init; }
}
