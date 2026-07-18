using System.Text;
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

        var tokens = SqlTokenizer.Tokenize(sql);

        var createIndex = SqlTokenizer.FindKeyword(tokens, "CREATE");
        if (createIndex < 0)
            return null;

        // Locate the "TABLE" keyword and the qualified name that follows it.
        var tableKeyword = SqlTokenizer.FindKeyword(tokens, "TABLE", createIndex);
        if (tableKeyword < 0)
            return null;

        var afterName = SqlTokenizer.ReadNameAfter(tokens, "TABLE", out var qualifiedName);
        if (qualifiedName is null)
            return null;

        var openParen = sql.IndexOf('(', afterName);
        if (openParen < 0)
            return null;

        var (schema, name) = SplitQualifiedName(qualifiedName);
        if (string.IsNullOrEmpty(name))
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
                return new ParsedTable { Schema = schema, Name = name, Columns = [], IsParseable = false };

            var column = ParseColumn(trimmed);
            if (column is null)
                return new ParsedTable { Schema = schema, Name = name, Columns = [], IsParseable = false };

            columns.Add(column);
        }

        return new ParsedTable { Schema = schema, Name = name, Columns = columns };
    }

    private static ParsedColumn? ParseColumn(string text)
    {
        var tokens = SqlTokenizer.Tokenize(text);

        var pos = 0;
        while (pos < tokens.Count && (tokens[pos].Kind == SqlTokenKind.Whitespace || tokens[pos].IsComment))
            pos++;

        if (pos >= tokens.Count || tokens[pos].Kind != SqlTokenKind.QuotedIdentifier)
            return null;

        var nameToken = tokens[pos];
        var name = UnquoteIdentifier(nameToken.Text);
        var definitionStart = nameToken.Start + nameToken.Length;
        var definition = text[definitionStart..].TrimStart();
        if (definition.Length == 0)
            return null;

        var clauses = ScanClauses(tokens, pos + 1);

        var typeEnd = clauses.Count > 0 ? clauses[0].Start : text.Length;
        var dataType = text[definitionStart..typeEnd].Trim();

        var notNull = false;
        string? defaultValue = null;
        string? collation = null;
        string? identity = null;

        for (var i = 0; i < clauses.Count; i++)
        {
            var (keyword, start) = clauses[i];
            var end = i + 1 < clauses.Count ? clauses[i + 1].Start : text.Length;
            var segment = text[start..end].Trim();

            switch (keyword)
            {
                case "NOT NULL":
                    notNull = true;
                    break;
                case "NULL":
                    notNull = false;
                    break;
                case "DEFAULT":
                    defaultValue = segment[keyword.Length..].Trim();
                    break;
                case "COLLATE":
                    collation = segment[keyword.Length..].Trim();
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
    private static List<(string Keyword, int Start)> ScanClauses(IReadOnlyList<SqlToken> tokens, int startTokenIndex)
    {
        var results = new List<(string, int)>();
        var depth = 0;

        for (var i = startTokenIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Kind == SqlTokenKind.Symbol && token.Text == "(")
            {
                depth++;
                continue;
            }

            if (token.Kind == SqlTokenKind.Symbol && token.Text == ")")
            {
                if (depth > 0)
                    depth--;
                continue;
            }

            if (depth != 0 || token.Kind != SqlTokenKind.Word)
                continue;

            // Handle NOT NULL as a single two-word clause and skip the NULL token.
            if (token.Text.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                var next = i + 1;
                while (next < tokens.Count && (tokens[next].Kind == SqlTokenKind.Whitespace || tokens[next].IsComment))
                    next++;

                if (next < tokens.Count && tokens[next].Kind == SqlTokenKind.Word && tokens[next].Text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(("NOT NULL", token.Start));
                    i = next;
                    continue;
                }
            }

            foreach (var keyword in ClauseKeywords)
            {
                if (token.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((keyword, token.Start));
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

        var tokens = SqlTokenizer.Tokenize(text);
        var pos = 0;
        while (pos < tokens.Count && (tokens[pos].Kind == SqlTokenKind.Whitespace || tokens[pos].IsComment))
            pos++;

        if (pos >= tokens.Count || tokens[pos].Kind != SqlTokenKind.Word)
            return false;

        var first = tokens[pos].Text;
        foreach (var keyword in keywords)
        {
            if (first.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string UnquoteIdentifier(string quoted)
    {
        if (quoted.Length >= 2 && quoted[0] == '"' && quoted[^1] == '"')
            return quoted[1..^1].Replace("\"\"", "\"");

        return quoted;
    }

    private static (string Schema, string Name) SplitQualifiedName(string qualifiedName)
    {
        var parts = SqlTokenizer.SplitTopLevel(qualifiedName, '.');
        if (parts.Count == 0)
            return ("", "");

        if (parts.Count == 1)
            return ("", UnquoteIdentifier(parts[0].Trim()));

        return (UnquoteIdentifier(parts[0].Trim()), UnquoteIdentifier(parts[^1].Trim()));
    }
}
