using System.Text;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Parses the <c>CREATE TABLE</c> statements produced by the exporter into a
/// structured <see cref="ParsedTable"/> so that the migration generator can emit
/// column-level <c>ALTER TABLE</c> statements instead of dropping and recreating.
/// </summary>
public static class TableDefinitionParser
{
    private static readonly string[] ClauseKeywords =
    [
        "NOT NULL", "NULL", "DEFAULT", "COLLATE", "GENERATED"
    ];

    public static ParsedTable? Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        var createIndex = sql.IndexOf("CREATE", StringComparison.OrdinalIgnoreCase);
        if (createIndex < 0)
            return null;

        // Locate the " TABLE " keyword and the qualified name that follows it.
        var tableKeyword = IndexOfWord(sql, "TABLE", createIndex);
        if (tableKeyword < 0)
            return null;

        var afterTable = tableKeyword + "TABLE".Length;

        // Skip an optional "IF NOT EXISTS".
        var ifNotExists = IndexOfWord(sql, "IF NOT EXISTS", afterTable);
        if (ifNotExists >= 0 && ifNotExists <= afterTable + 2)
            afterTable = ifNotExists + "IF NOT EXISTS".Length;

        var openParen = sql.IndexOf('(', afterTable);
        if (openParen < 0)
            return null;

        var qualifiedName = sql[afterTable..openParen].Trim();
        if (qualifiedName.Length == 0)
            return null;

        var closeParen = FindMatchingParen(sql, openParen);
        if (closeParen < 0)
            return null;

        var body = sql[(openParen + 1)..closeParen];
        var entries = SplitTopLevel(body);

        var columns = new List<ParsedColumn>();
        foreach (var entry in entries)
        {
            var trimmed = entry.Trim().TrimEnd(',').Trim();
            if (trimmed.Length == 0)
                continue;

            // Inline table constraints are not handled semantically; bail out so the
            // caller can fall back to a safe drop/recreate strategy.
            if (StartsWithConstraintKeyword(trimmed))
                return new ParsedTable { QualifiedName = qualifiedName, Columns = [], IsParseable = false };

            if (trimmed[0] != '"')
                return new ParsedTable { QualifiedName = qualifiedName, Columns = [], IsParseable = false };

            var column = ParseColumn(trimmed);
            if (column is null)
                return new ParsedTable { QualifiedName = qualifiedName, Columns = [], IsParseable = false };

            columns.Add(column);
        }

        return new ParsedTable { QualifiedName = qualifiedName, Columns = columns };
    }

    private static ParsedColumn? ParseColumn(string text)
    {
        var nameEnd = ReadQuotedIdentifierEnd(text);
        if (nameEnd < 0)
            return null;

        var name = UnquoteIdentifier(text[..(nameEnd + 1)]);
        var definition = text[(nameEnd + 1)..].Trim();
        if (definition.Length == 0)
            return null;

        var clauses = ScanClauses(definition);

        var typeEnd = clauses.Count > 0 ? clauses[0].Start : definition.Length;
        var dataType = definition[..typeEnd].Trim();

        var notNull = false;
        string? defaultValue = null;
        string? collation = null;
        string? identity = null;

        for (var i = 0; i < clauses.Count; i++)
        {
            var (keyword, start) = clauses[i];
            var end = i + 1 < clauses.Count ? clauses[i + 1].Start : definition.Length;
            var segment = definition[start..end].Trim();

            switch (keyword)
            {
                case "NOT NULL":
                    notNull = true;
                    break;
                case "NULL":
                    notNull = false;
                    break;
                case "DEFAULT":
                    defaultValue = segment["DEFAULT".Length..].Trim();
                    break;
                case "COLLATE":
                    collation = segment["COLLATE".Length..].Trim();
                    break;
                case "GENERATED":
                    identity = segment;
                    break;
            }
        }

        return new ParsedColumn
        {
            Name = name,
            DataType = dataType,
            NotNull = notNull,
            Default = defaultValue,
            Collation = collation,
            Identity = identity,
            Definition = definition
        };
    }

    /// <summary>
    /// Returns the clause keywords found at the top level of a column definition
    /// (outside quotes and parentheses), with their start offsets, in order.
    /// </summary>
    private static List<(string Keyword, int Start)> ScanClauses(string definition)
    {
        var results = new List<(string, int)>();
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < definition.Length; i++)
        {
            var c = definition[i];

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
                case '\'': inSingle = true; continue;
                case '"': inDouble = true; continue;
                case '(': depth++; continue;
                case ')': depth--; continue;
            }

            if (depth != 0)
                continue;

            foreach (var keyword in ClauseKeywords)
            {
                if (MatchesWordAt(definition, i, keyword))
                {
                    // Avoid double-matching "NULL" when it is part of "NOT NULL".
                    if (keyword == "NULL" && results.Count > 0 && results[^1].Item1 == "NOT NULL")
                    {
                        var prevEnd = results[^1].Item2 + "NOT NULL".Length;
                        if (i < prevEnd)
                            break;
                    }

                    results.Add((keyword, i));
                    break;
                }
            }
        }

        return results;
    }

    private static bool MatchesWordAt(string text, int index, string word)
    {
        if (index + word.Length > text.Length)
            return false;

        if (!string.Equals(text.Substring(index, word.Length), word, StringComparison.OrdinalIgnoreCase))
            return false;

        if (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_'))
            return false;

        var after = index + word.Length;
        if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
            return false;

        return true;
    }

    private static bool StartsWithConstraintKeyword(string text)
    {
        ReadOnlySpan<string> keywords =
        [
            "CONSTRAINT", "PRIMARY", "UNIQUE", "CHECK", "FOREIGN", "EXCLUDE", "LIKE"
        ];

        foreach (var keyword in keywords)
        {
            if (MatchesWordAt(text, 0, keyword))
                return true;
        }

        return false;
    }

    private static int ReadQuotedIdentifierEnd(string text)
    {
        if (text.Length == 0 || text[0] != '"')
            return -1;

        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] != '"')
                continue;

            // Escaped quote ("") inside the identifier.
            if (i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    private static string UnquoteIdentifier(string quoted)
    {
        if (quoted.Length >= 2 && quoted[0] == '"' && quoted[^1] == '"')
            return quoted[1..^1].Replace("\"\"", "\"");

        return quoted;
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
        var current = new StringBuilder();
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

    private static int IndexOfWord(string text, string word, int start)
    {
        var i = start;
        while (i >= 0 && i <= text.Length - word.Length)
        {
            var found = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (MatchesWordAt(text, found, word))
                return found;

            i = found + 1;
        }

        return -1;
    }
}
