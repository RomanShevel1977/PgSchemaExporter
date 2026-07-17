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

        var fk = FindKeyword(definition, "FOREIGN KEY");
        if (fk >= 0)
            return ParseForeignKey(definition, fk);

        var pk = FindKeyword(definition, "PRIMARY KEY");
        if (pk >= 0)
            return new ParsedConstraint
            {
                Kind = ConstraintKind.PrimaryKey,
                Columns = ReadColumnList(definition, pk + "PRIMARY KEY".Length)
            };

        var unique = FindKeyword(definition, "UNIQUE");
        if (unique >= 0)
            return new ParsedConstraint
            {
                Kind = ConstraintKind.Unique,
                Columns = ReadColumnList(definition, unique + "UNIQUE".Length)
            };

        return new ParsedConstraint { Kind = ConstraintKind.Other };
    }

    private static ParsedConstraint ParseForeignKey(string definition, int fkIndex)
    {
        var columns = ReadColumnList(definition, fkIndex + "FOREIGN KEY".Length, out var afterColumns);

        var references = FindKeyword(definition, "REFERENCES", afterColumns);
        if (references < 0)
            return new ParsedConstraint { Kind = ConstraintKind.ForeignKey, Columns = columns };

        var cursor = references + "REFERENCES".Length;
        var referencedTable = ReadQualifiedName(definition, cursor, out var afterName);
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

    /// <summary>
    /// Reads a (possibly schema-qualified, possibly quoted) table name, e.g.
    /// <c>"public"."users"</c>, <c>public.users</c>, or <c>users</c>.
    /// </summary>
    private static string ReadQualifiedName(string text, int start, out int afterName)
    {
        var i = start;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        current.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuote = false;
                    i++;
                    continue;
                }
                current.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuote = true;
                i++;
                continue;
            }

            if (c == '.')
            {
                parts.Add(current.ToString());
                current.Clear();
                i++;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
            {
                current.Append(c);
                i++;
                continue;
            }

            break;
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        afterName = i;
        return string.Join('.', parts.Where(p => p.Length > 0));
    }

    /// <summary>Finds a keyword as a whole word, ignoring case and collapsing internal spaces.</summary>
    private static int FindKeyword(string text, string keyword, int start = 0)
    {
        // Keywords contain at most one space (e.g. "FOREIGN KEY", "PRIMARY KEY").
        var spaceIndex = keyword.IndexOf(' ');
        var firstWord = spaceIndex >= 0 ? keyword.AsSpan(0, spaceIndex) : keyword.AsSpan();
        var secondWord = spaceIndex >= 0 ? keyword.AsSpan(spaceIndex + 1) : ReadOnlySpan<char>.Empty;

        var minLength = firstWord.Length + (spaceIndex >= 0 ? 1 : 0) + secondWord.Length;
        var i = start;

        while (i <= text.Length - minLength)
        {
            var foundAt = text.AsSpan(i).IndexOf(firstWord, StringComparison.OrdinalIgnoreCase);
            if (foundAt < 0)
                return -1;

            var candidate = i + foundAt;

            if (candidate > 0 && IsWordChar(text[candidate - 1]))
            {
                i = candidate + 1;
                continue;
            }

            var pos = candidate + firstWord.Length;

            // Single-word keyword; check right boundary.
            if (spaceIndex < 0)
            {
                if (pos < text.Length && IsWordChar(text[pos]))
                {
                    i = candidate + 1;
                    continue;
                }

                return candidate;
            }

            // Multi-word keyword: require whitespace, then second word.
            if (pos >= text.Length || !char.IsWhiteSpace(text[pos]))
            {
                i = candidate + 1;
                continue;
            }

            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                pos++;

            if (pos + secondWord.Length > text.Length)
                return -1;

            if (!text.AsSpan(pos, secondWord.Length).Equals(secondWord, StringComparison.OrdinalIgnoreCase))
            {
                i = candidate + 1;
                continue;
            }

            var after = pos + secondWord.Length;
            if (after < text.Length && IsWordChar(text[after]))
            {
                i = candidate + 1;
                continue;
            }

            return candidate;
        }

        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string Unquote(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\"\"", "\"");

        return trimmed;
    }
}
