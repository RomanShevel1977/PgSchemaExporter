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

        var tokens = SqlTokenizer.Tokenize(definition);

        var fk = SqlTokenizer.FindKeyword(tokens, "FOREIGN KEY");
        if (fk >= 0)
            return ParseForeignKey(definition, tokens, fk);

        var pk = SqlTokenizer.FindKeyword(tokens, "PRIMARY KEY");
        if (pk >= 0)
            return new ParsedConstraint
            {
                Kind = ConstraintKind.PrimaryKey,
                Columns = ReadColumnList(definition, tokens, pk)
            };

        var unique = SqlTokenizer.FindKeyword(tokens, "UNIQUE");
        if (unique >= 0)
            return new ParsedConstraint
            {
                Kind = ConstraintKind.Unique,
                Columns = ReadColumnList(definition, tokens, unique)
            };

        return new ParsedConstraint { Kind = ConstraintKind.Other };
    }

    private static ParsedConstraint ParseForeignKey(string definition, IReadOnlyList<SqlToken> tokens, int fkIndex)
    {
        var columns = ReadColumnList(definition, tokens, fkIndex, out var afterColumns);

        var references = SqlTokenizer.FindKeyword(tokens, "REFERENCES", afterColumns);
        if (references < 0)
            return new ParsedConstraint { Kind = ConstraintKind.ForeignKey, Columns = columns };

        var referencedTableRaw = SqlTokenizer.ReadIdentifier(tokens, references + 1, out var afterName);
        var referencedTable = UnquoteQualified(referencedTableRaw);
        var referencedColumns = ReadColumnList(definition, tokens, afterName);

        return new ParsedConstraint
        {
            Kind = ConstraintKind.ForeignKey,
            Columns = columns,
            ReferencedTable = referencedTable,
            ReferencedColumns = referencedColumns
        };
    }

    private static IReadOnlyList<string> ReadColumnList(string sql, IReadOnlyList<SqlToken> tokens, int startTokenIndex)
        => ReadColumnList(sql, tokens, startTokenIndex, out _);

    /// <summary>
    /// Reads the parenthesized, comma-separated identifier list that starts at or
    /// after <paramref name="startTokenIndex"/>. Returns an empty list if none is found.
    /// </summary>
    private static IReadOnlyList<string> ReadColumnList(string sql, IReadOnlyList<SqlToken> tokens, int startTokenIndex, out int afterTokenIndex)
    {
        afterTokenIndex = startTokenIndex;

        var inner = SqlTokenizer.ReadParenthesized(tokens, sql, startTokenIndex, out afterTokenIndex);
        if (inner is null)
            return [];

        return SqlTokenizer.SplitTopLevel(inner)
            .Select(part => Unquote(part.Trim()))
            .Where(part => part.Length > 0)
            .ToList();
    }

    private static string? UnquoteQualified(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var parts = SqlTokenizer.SplitTopLevel(raw, '.');
        return string.Join('.', parts.Select(Unquote).Where(p => p.Length > 0));
    }

    private static string Unquote(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\"\"", "\"");

        return trimmed;
    }
}
