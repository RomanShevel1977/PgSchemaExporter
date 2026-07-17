using PgSchemaExporter.Core.Scripting;

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

        var createIndex = SqlTokenizer.IndexOfWord(sql, "CREATE", 0);
        if (createIndex < 0)
            return null;

        // Locate the " TABLE " keyword and the qualified name that follows it.
        var tableKeyword = SqlTokenizer.IndexOfWord(sql, "TABLE", createIndex, out var tableLength);
        if (tableKeyword < 0)
            return null;

        var afterTable = tableKeyword + tableLength;

        // Skip optional leading noise (IF NOT EXISTS, ONLY) before the table name.
        var remaining = sql[afterTable..];
        while (true)
        {
            var trimmed = remaining.TrimStart();
            var skippedWhitespace = remaining.Length - trimmed.Length;

            if (SqlTokenizer.StartsWithWord(trimmed, "IF NOT EXISTS"))
            {
                afterTable += skippedWhitespace + "IF NOT EXISTS".Length;
                remaining = sql[afterTable..];
                continue;
            }

            if (SqlTokenizer.StartsWithWord(trimmed, "ONLY"))
            {
                afterTable += skippedWhitespace + "ONLY".Length;
                remaining = sql[afterTable..];
                continue;
            }

            break;
        }

        var qualifiedName = SqlTokenizer.ReadIdentifier(sql, afterTable, out var afterName, unquote: false);
        if (string.IsNullOrEmpty(qualifiedName))
            return null;

        var openParen = sql.IndexOf('(', afterName);
        if (openParen < 0)
            return null;

        var closeParen = SqlTokenizer.FindMatchingParen(sql, openParen);
        if (closeParen < 0)
            return null;

        var body = sql[(openParen + 1)..closeParen];
        var entries = SqlTokenizer.SplitTopLevel(body);

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
        var name = SqlTokenizer.ReadIdentifier(text, 0, out var nameEnd, unquote: true);
        if (name is null)
            return null;

        var definition = text[nameEnd..].Trim();
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
                if (SqlTokenizer.MatchesWordAt(definition, i, keyword))
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

    private static bool StartsWithConstraintKeyword(string text)
    {
        ReadOnlySpan<string> keywords =
        [
            "CONSTRAINT", "PRIMARY", "UNIQUE", "CHECK", "FOREIGN", "EXCLUDE", "LIKE"
        ];

        foreach (var keyword in keywords)
        {
            if (SqlTokenizer.MatchesWordAt(text, 0, keyword))
                return true;
        }

        return false;
    }
}
