namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// A structured representation of a single column parsed from a generated
/// <c>CREATE TABLE</c> statement, used to produce column-level ALTER migrations.
/// </summary>
public sealed class ParsedColumn
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool NotNull { get; init; }
    public string? Default { get; init; }
    public string? Collation { get; init; }
    public string? Identity { get; init; }

    /// <summary>The full definition text after the column name, used for ADD COLUMN.</summary>
    public required string Definition { get; init; }
}

/// <summary>
/// A parsed <c>CREATE TABLE</c> statement: the qualified table name and its columns.
/// <see cref="IsParseable"/> is false when the statement contains constructs we do
/// not safely understand (e.g. inline table constraints), in which case callers
/// should fall back to a drop/recreate strategy.
/// </summary>
public sealed class ParsedTable
{
    public required string QualifiedName { get; init; }
    public required IReadOnlyList<ParsedColumn> Columns { get; init; }
    public bool IsParseable { get; init; } = true;
}
