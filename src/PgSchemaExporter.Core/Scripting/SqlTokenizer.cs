using System.Text;

namespace PgSchemaExporter.Core.Scripting;

/// <summary>
/// Centralized, allocation-conscious SQL tokenizer.  It is responsible for the
/// single-pass scanning that used to live inside <see cref="SqlStatementSplitter"/>
/// and can be reused by other SQL parsers to avoid duplicated parsing passes.
/// </summary>
public static class SqlTokenizer
{
    /// <summary>
    /// Splits a SQL batch into individual statements respecting single/double
    /// quotes, dollar-quoted blocks, and line/block comments.
    /// </summary>
    public static IReadOnlyList<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder(Math.Min(sql.Length, 4096));

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarTag = null;

        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                current.Append(ch);
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                current.Append(ch);
                if (ch == '*' && next == '/')
                {
                    current.Append(next);
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (dollarTag is not null)
            {
                if (ch == '$' && IsAt(sql, i, dollarTag))
                {
                    current.Append(dollarTag);
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (inSingleQuote)
            {
                current.Append(ch);

                if (ch == '\'' && next == '\'')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                current.Append(ch);

                if (ch == '"' && next == '"')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '-' && next == '-')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inLineComment = true;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inBlockComment = true;
                continue;
            }

            if (ch == '\'')
            {
                current.Append(ch);
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                current.Append(ch);
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$')
            {
                var tag = TryReadDollarTag(sql, i);
                if (tag is not null)
                {
                    current.Append(tag);
                    i += tag.Length - 1;
                    dollarTag = tag;
                    continue;
                }
            }

            if (ch == ';')
            {
                current.Append(ch);

                var raw = current.ToString();
                var statement = raw.AsSpan().Trim();
                if (!statement.IsEmpty)
                    statements.Add(statement.Length == raw.Length ? raw : statement.ToString());

                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tailRaw = current.ToString();
        var tail = tailRaw.AsSpan().Trim();
        if (!tail.IsEmpty)
            statements.Add(tail.Length == tailRaw.Length ? tailRaw : tail.ToString());

        return statements;
    }

    /// <summary>
    /// Collapses all whitespace runs to a single space and trims the statement.
    /// Produces a canonical form that can be used for ordinal comparison while
    /// preserving lexical identity.
    /// </summary>
    public static string NormalizeStatement(string sql)
    {
        var span = sql.AsSpan().Trim();
        if (span.IsEmpty)
            return "";

        var sb = new StringBuilder(span.Length);
        var lastWasSpace = false;

        foreach (var c in span)
        {
            if (char.IsWhiteSpace(c))
            {
                lastWasSpace = true;
                continue;
            }

            if (lastWasSpace && sb.Length > 0)
                sb.Append(' ');

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds the matching closing parenthesis for the opening parenthesis at
    /// <paramref name="openIndex"/>, ignoring parentheses inside quoted literals,
    /// comments and dollar-quoted blocks.
    /// </summary>
    public static int FindMatchingParen(string text, int openIndex)
    {
        if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != '(')
            return -1;

        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarTag = null;

        for (var i = openIndex; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (dollarTag is not null)
            {
                if (ch == '$' && IsAt(text, i, dollarTag))
                {
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                }
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'' && next == '\'')
                {
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '"' && next == '"')
                {
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '-' && next == '-')
            {
                i++;
                inLineComment = true;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                i++;
                inBlockComment = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$')
            {
                var tag = TryReadDollarTag(text, i);
                if (tag is not null)
                {
                    i += tag.Length - 1;
                    dollarTag = tag;
                    continue;
                }
            }

            if (ch == '(')
                depth++;
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits <paramref name="text"/> by <paramref name="delimiter"/> only at the
    /// top level, outside quoted literals, comments and dollar-quoted blocks.
    /// Useful for column lists, function arguments and comma-separated clauses.
    /// </summary>
    public static IReadOnlyList<string> SplitTopLevel(string text, char delimiter = ',')
    {
        var parts = new List<string>();
        var current = new StringBuilder(text.Length);

        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarTag = null;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                current.Append(ch);
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                current.Append(ch);
                if (ch == '*' && next == '/')
                {
                    current.Append(next);
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (dollarTag is not null)
            {
                if (ch == '$' && IsAt(text, i, dollarTag))
                {
                    current.Append(dollarTag);
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (inSingleQuote)
            {
                current.Append(ch);

                if (ch == '\'' && next == '\'')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                current.Append(ch);

                if (ch == '"' && next == '"')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '-' && next == '-')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inLineComment = true;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inBlockComment = true;
                continue;
            }

            if (ch == '\'')
            {
                current.Append(ch);
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                current.Append(ch);
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$')
            {
                var tag = TryReadDollarTag(text, i);
                if (tag is not null)
                {
                    current.Append(tag);
                    i += tag.Length - 1;
                    dollarTag = tag;
                    continue;
                }
            }

            if (ch == '(')
            {
                depth++;
                current.Append(ch);
            }
            else if (ch == ')')
            {
                depth--;
                current.Append(ch);
            }
            else if (ch == delimiter && depth == 0)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString().Trim());

        return parts;
    }

    private static string? TryReadDollarTag(string sql, int start)
    {
        var end = start + 1;

        while (end < sql.Length)
        {
            var ch = sql[end];

            if (ch == '$')
                return sql[start..(end + 1)];

            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return null;

            end++;
        }

        return null;
    }

    private static bool IsAt(string sql, int index, string value)
    {
        if (index + value.Length > sql.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (sql[index + i] != value[i])
                return false;
        }

        return true;
    }
}
