using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Diagramming;

public enum ConstraintKind
{
    Other,
    PrimaryKey,
    Unique,
    ForeignKey
}

/// <summary>
/// Parsed representation of a table constraint definition. Handles both the output
/// of <c>pg_get_constraintdef</c> (live database) and the definition portion of an
/// exported <c>ALTER TABLE ... ADD CONSTRAINT</c> statement.
/// </summary>
public sealed class ParsedConstraint
{
    public ConstraintKind Kind { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Referenced table for foreign keys (schema-qualified when present), else null.</summary>
    public string? ReferencedTable { get; init; }
    public IReadOnlyList<string> ReferencedColumns { get; init; } = [];
}

/// <summary>
/// Extracts the column lists and (for foreign keys) the referenced table from a
/// constraint definition, tolerant of quoting and trailing clauses such as
/// <c>ON DELETE CASCADE</c> or <c>DEFERRABLE</c>.
/// </summary>
public static class ConstraintDefinitionParser
{
    public static ParsedConstraint Parse(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return new ParsedConstraint { Kind = ConstraintKind.Other };

        var fk = SqlTokenizer.IndexOfWord(definition, "FOREIGN KEY", 0, out var fkLength);
        if (fk >= 0)
            return ParseForeignKey(definition, fk, fkLength);

        var pk = SqlTokenizer.IndexOfWord(definition, "PRIMARY KEY", 0, out var pkLength);
        if (pk >= 0)
            return new ParsedConstraint
            {
                Kind = ConstraintKind.PrimaryKey,
                Columns = ReadColumnList(definition, pk + pkLength)
            };

        var unique = SqlTokenizer.IndexOfWord(definition, "UNIQUE", 0, out var uniqueLength);
        if (unique >= 0)
            return new ParsedConstraint
            {
                Kind = ConstraintKind.Unique,
                Columns = ReadColumnList(definition, unique + uniqueLength)
            };

        return new ParsedConstraint { Kind = ConstraintKind.Other };
    }

    private static ParsedConstraint ParseForeignKey(string definition, int fkIndex, int fkLength)
    {
        var columns = ReadColumnList(definition, fkIndex + fkLength, out var afterColumns);

        var references = SqlTokenizer.IndexOfWord(definition, "REFERENCES", afterColumns, out var referencesLength);
        if (references < 0)
            return new ParsedConstraint { Kind = ConstraintKind.ForeignKey, Columns = columns };

        var cursor = references + referencesLength;
        var referencedTable = SqlTokenizer.ReadIdentifier(definition, cursor, out var afterName, unquote: true);
        var referencedColumns = ReadColumnList(definition, afterName);

        return new ParsedConstraint
        {
            Kind = ConstraintKind.ForeignKey,
            Columns = columns,
            ReferencedTable = referencedTable,
            ReferencedColumns = referencedColumns
        };
    }

    private static IReadOnlyList<string> ReadColumnList(string text, int start)
        => ReadColumnList(text, start, out _);

    /// <summary>
    /// Reads the parenthesized, comma-separated identifier list that starts at or
    /// after <paramref name="start"/>. Returns an empty list if none is found.
    /// </summary>
    private static IReadOnlyList<string> ReadColumnList(string text, int start, out int afterList)
    {
        afterList = start;

        var open = text.IndexOf('(', start);
        if (open < 0)
            return [];

        var close = SqlTokenizer.FindMatchingParen(text, open);
        if (close < 0)
            return [];

        afterList = close + 1;
        var inner = text[(open + 1)..close];

        return SqlTokenizer.SplitTopLevel(inner)
            .Select(part => Unquote(part.Trim()))
            .Where(part => part.Length > 0)
            .ToList();
    }


    private static string Unquote(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\"\"", "\"");

        return trimmed;
    }
}
