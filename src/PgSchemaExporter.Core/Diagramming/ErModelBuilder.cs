using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Diagramming;

/// <summary>
/// Builds an <see cref="ErModel"/> from either a live <see cref="DatabaseModel"/>
/// or an exported schema directory. Foreign keys, primary keys, unique constraints
/// and column nullability are resolved so the renderers can draw meaningful
/// cardinalities and key markers.
/// </summary>
public static class ErModelBuilder
{
    private sealed record RawColumn(string Name, string DataType, bool IsNullable);

    private sealed record RawTable(string Schema, string Name, IReadOnlyList<RawColumn> Columns)
    {
        public string Qualified => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
    }

    private sealed record RawConstraint(string TableSchema, string TableName, ParsedConstraint Parsed)
    {
        public string TableQualified => string.IsNullOrEmpty(TableSchema) ? TableName : $"{TableSchema}.{TableName}";
    }

    public static ErModel FromDatabaseModel(DatabaseModel model)
    {
        var tables = model.Tables
            .Select(t => new RawTable(
                t.Schema,
                t.Name,
                t.Columns
                    .OrderBy(c => c.OrdinalPosition)
                    .Select(c => new RawColumn(c.Name, c.DataType, c.IsNullable))
                    .ToList()))
            .ToList();

        var constraints = model.Constraints
            .Select(c => new RawConstraint(c.Schema, c.TableName, ConstraintDefinitionParser.Parse(c.Definition)))
            .ToList();

        return Build(tables, constraints);
    }

    public static ErModel FromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Schema directory was not found: {directory}");

        var tables = ReadDirectoryTables(directory);
        var constraints = ReadDirectoryConstraints(directory);

        return Build(tables, constraints);
    }

    private static List<RawTable> ReadDirectoryTables(string directory)
    {
        var tablesDir = Path.Combine(directory, "tables");
        var result = new List<RawTable>();

        if (!Directory.Exists(tablesDir))
            return result;

        foreach (var file in Directory.EnumerateFiles(tablesDir, "*.sql", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            var parsed = TableDefinitionParser.Parse(content);
            if (parsed is null)
                continue;

            var (schema, name) = SplitQualifiedName(parsed.QualifiedName);
            if (string.IsNullOrEmpty(name))
                continue;

            var columns = parsed.Columns
                .Select(c => new RawColumn(c.Name, c.DataType, !c.NotNull))
                .ToList();

            result.Add(new RawTable(schema, name, columns));
        }

        return result;
    }

    private static List<RawConstraint> ReadDirectoryConstraints(string directory)
    {
        var constraintsDir = Path.Combine(directory, "constraints");
        var result = new List<RawConstraint>();

        if (!Directory.Exists(constraintsDir))
            return result;

        foreach (var file in Directory.EnumerateFiles(constraintsDir, "*.sql", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);

            foreach (var statement in content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (statement.IndexOf("ADD CONSTRAINT", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var owner = ExtractOwnerTable(statement);
                if (owner is null)
                    continue;

                var parsed = ConstraintDefinitionParser.Parse(statement);
                if (parsed.Kind == ConstraintKind.Other)
                    continue;

                result.Add(new RawConstraint(owner.Value.Schema, owner.Value.Name, parsed));
            }
        }

        return result;
    }

    private static ErModel Build(IReadOnlyList<RawTable> rawTables, IReadOnlyList<RawConstraint> rawConstraints)
    {
        // Index tables by qualified name and by bare name for FK target resolution.
        var byQualified = new Dictionary<string, RawTable>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<RawTable>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in rawTables)
        {
            byQualified[table.Qualified] = table;
            if (!byName.TryGetValue(table.Name, out var list))
                byName[table.Name] = list = [];
            list.Add(table);
        }

        // Collect key/column roles per table.
        var pkColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var uniqueColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var fkColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(Dictionary<string, HashSet<string>> map, string table, IEnumerable<string> cols)
        {
            if (!map.TryGetValue(table, out var set))
                map[table] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in cols)
                set.Add(col);
        }

        var relationships = new List<ErRelationship>();
        var relationshipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var constraint in rawConstraints)
        {
            var owner = constraint.TableQualified;

            switch (constraint.Parsed.Kind)
            {
                case ConstraintKind.PrimaryKey:
                    Add(pkColumns, owner, constraint.Parsed.Columns);
                    break;

                case ConstraintKind.Unique:
                    Add(uniqueColumns, owner, constraint.Parsed.Columns);
                    break;

                case ConstraintKind.ForeignKey:
                    Add(fkColumns, owner, constraint.Parsed.Columns);
                    var resolved = ResolveTarget(constraint.Parsed.ReferencedTable, constraint.TableSchema, byQualified, byName);
                    if (resolved is null)
                        break;

                    var isMandatory = ColumnsAreNotNull(byQualified, owner, constraint.Parsed.Columns);
                    var key = $"{owner}=>{resolved}:{string.Join(',', constraint.Parsed.Columns)}";
                    if (relationshipKeys.Add(key))
                    {
                        relationships.Add(new ErRelationship
                        {
                            FromTable = owner,
                            FromColumns = constraint.Parsed.Columns,
                            ToTable = resolved,
                            ToColumns = constraint.Parsed.ReferencedColumns,
                            IsMandatory = isMandatory
                        });
                    }
                    break;
            }
        }

        var erTables = rawTables
            .OrderBy(t => t.Schema, StringComparer.Ordinal)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t =>
            {
                pkColumns.TryGetValue(t.Qualified, out var pk);
                uniqueColumns.TryGetValue(t.Qualified, out var uq);
                fkColumns.TryGetValue(t.Qualified, out var fk);

                var columns = t.Columns.Select(c => new ErColumn
                {
                    Name = c.Name,
                    DataType = c.DataType,
                    IsNullable = c.IsNullable,
                    IsPrimaryKey = pk?.Contains(c.Name) == true,
                    IsUnique = uq?.Contains(c.Name) == true,
                    IsForeignKey = fk?.Contains(c.Name) == true
                }).ToList();

                return new ErTable { Schema = t.Schema, Name = t.Name, Columns = columns };
            })
            .ToList();

        return new ErModel
        {
            Tables = erTables,
            Relationships = relationships
                .OrderBy(r => r.FromTable, StringComparer.Ordinal)
                .ThenBy(r => r.ToTable, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static bool ColumnsAreNotNull(
        IReadOnlyDictionary<string, RawTable> byQualified,
        string tableQualified,
        IReadOnlyList<string> columns)
    {
        if (columns.Count == 0 || !byQualified.TryGetValue(tableQualified, out var table))
            return false;

        foreach (var col in columns)
        {
            var match = table.Columns.FirstOrDefault(c => string.Equals(c.Name, col, StringComparison.OrdinalIgnoreCase));
            if (match is null || match.IsNullable)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a foreign-key target to a known table's qualified name. A qualified
    /// reference must match exactly; a bare name prefers a table in the referencing
    /// table's own schema, then falls back to a unique match.
    /// </summary>
    private static string? ResolveTarget(
        string? referenced,
        string referencingSchema,
        IReadOnlyDictionary<string, RawTable> byQualified,
        IReadOnlyDictionary<string, List<RawTable>> byName)
    {
        if (string.IsNullOrWhiteSpace(referenced))
            return null;

        if (referenced.Contains('.'))
            return byQualified.TryGetValue(referenced, out var exact) ? exact.Qualified : null;

        if (!byName.TryGetValue(referenced, out var matches) || matches.Count == 0)
            return null;

        var sameSchema = matches.FirstOrDefault(t => string.Equals(t.Schema, referencingSchema, StringComparison.OrdinalIgnoreCase));
        if (sameSchema is not null)
            return sameSchema.Qualified;

        return matches.Count == 1 ? matches[0].Qualified : null;
    }

    /// <summary>
    /// Extracts the owning table of an <c>ALTER TABLE [ONLY] &lt;name&gt; ADD CONSTRAINT ...</c>
    /// statement.
    /// </summary>
    private static (string Schema, string Name)? ExtractOwnerTable(string statement)
    {
        var tableIdx = statement.IndexOf("TABLE", StringComparison.OrdinalIgnoreCase);
        var addIdx = statement.IndexOf("ADD CONSTRAINT", StringComparison.OrdinalIgnoreCase);
        if (tableIdx < 0 || addIdx < 0 || addIdx <= tableIdx)
            return null;

        var between = statement[(tableIdx + "TABLE".Length)..addIdx].Trim();

        // Strip a leading ONLY keyword.
        if (between.StartsWith("ONLY", StringComparison.OrdinalIgnoreCase) &&
            (between.Length == 4 || char.IsWhiteSpace(between[4])))
        {
            between = between[4..].Trim();
        }

        var (schema, name) = SplitQualifiedName(between);
        return string.IsNullOrEmpty(name) ? null : (schema, name);
    }

    /// <summary>
    /// Splits a (possibly quoted) qualified name such as <c>"public"."users"</c>,
    /// <c>public.users</c>, or <c>users</c> into its schema and table parts.
    /// </summary>
    private static (string Schema, string Name) SplitQualifiedName(string qualified)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;

        for (var i = 0; i < qualified.Length; i++)
        {
            var c = qualified[i];

            if (inQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < qualified.Length && qualified[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                        continue;
                    }
                    inQuote = false;
                    continue;
                }
                current.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuote = true;
                    break;
                case '.':
                    parts.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    if (!char.IsWhiteSpace(c))
                        current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        parts = parts.Where(p => p.Length > 0).ToList();

        return parts.Count switch
        {
            0 => ("", ""),
            1 => ("", parts[0]),
            _ => (parts[^2], parts[^1])
        };
    }
}
