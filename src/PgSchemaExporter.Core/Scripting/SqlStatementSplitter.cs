using System.Text;

namespace PgSchemaExporter.Core.Scripting;

public sealed class SqlStatementSplitter
{
    public IReadOnlyList<string> Split(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder();

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

                var statement = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                    statements.Add(statement);

                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            statements.Add(tail);

        return statements;
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
