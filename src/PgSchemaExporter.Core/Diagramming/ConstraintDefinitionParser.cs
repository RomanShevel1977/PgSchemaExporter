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

        var close = FindMatchingParen(text, open);
        if (close < 0)
            return [];

        afterList = close + 1;
        var inner = text[(open + 1)..close];

        return SplitTopLevel(inner)
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
        // Keyword may contain a single space (e.g. "FOREIGN KEY"); match flexible whitespace.
        var words = keyword.Split(' ');
        var i = start;

        while (i < text.Length)
        {
            var match = TryMatchWords(text, i, words);
            if (match >= 0)
                return i;
            i++;
        }

        return -1;
    }

    /// <summary>
    /// Attempts to match the sequence of <paramref name="words"/> at <paramref name="index"/>,
    /// allowing one-or-more whitespace between words and requiring word boundaries.
    /// Returns the index past the match, or -1.
    /// </summary>
    private static int TryMatchWords(string text, int index, string[] words)
    {
        if (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_'))
            return -1;

        var pos = index;
        for (var w = 0; w < words.Length; w++)
        {
            var word = words[w];
            if (pos + word.Length > text.Length)
                return -1;

            if (!string.Equals(text.Substring(pos, word.Length), word, StringComparison.OrdinalIgnoreCase))
                return -1;

            pos += word.Length;

            if (w < words.Length - 1)
            {
                var wsStart = pos;
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;
                if (pos == wsStart)
                    return -1;
            }
        }

        if (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_'))
            return -1;

        return pos;
    }

    private static int FindMatchingParen(string text, int openIndex)
    {
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inSingle)
            {
                if (c == '\'') inSingle = false;
                continue;
            }

            if (inDouble)
            {
                if (c == '"') inDouble = false;
                continue;
            }

            switch (c)
            {
                case '\'': inSingle = true; break;
                case '"': inDouble = true; break;
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth == 0)
                        return i;
                    break;
            }
        }

        return -1;
    }

    private static List<string> SplitTopLevel(string body)
    {
        var entries = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        foreach (var c in body)
        {
            if (inSingle)
            {
                current.Append(c);
                if (c == '\'') inSingle = false;
                continue;
            }

            if (inDouble)
            {
                current.Append(c);
                if (c == '"') inDouble = false;
                continue;
            }

            switch (c)
            {
                case '\'': inSingle = true; current.Append(c); break;
                case '"': inDouble = true; current.Append(c); break;
                case '(': depth++; current.Append(c); break;
                case ')': depth--; current.Append(c); break;
                case ',' when depth == 0:
                    entries.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
            entries.Add(current.ToString());

        return entries;
    }

    private static string Unquote(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\"\"", "\"");

        return trimmed;
    }
}
