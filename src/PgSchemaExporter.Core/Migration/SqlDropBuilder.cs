using System.Text;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Builds rollback (DROP / REVOKE) statements from the CREATE statements found in an
/// exported object file. Used to generate down migrations and to drop the old
/// definition when an object that cannot be replaced in place has changed.
/// </summary>
public static class SqlDropBuilder
{
    /// <summary>
    /// Produces the inverse statements for a given file's content. Returns one drop
    /// statement per CREATE statement, in reverse order so dependent objects drop first.
    /// </summary>
    public static IReadOnlyList<string> BuildDrops(MigrationObjectKind kind, IEnumerable<string> statements)
    {
        var drops = new List<string>();

        foreach (var statement in statements)
        {
            var drop = BuildDrop(kind, statement);
            if (drop is not null)
                drops.Add(drop);
        }

        drops.Reverse();
        return drops;
    }

    public static string? BuildDrop(MigrationObjectKind kind, string statement)
    {
        var sql = statement.Trim();

        return kind switch
        {
            MigrationObjectKind.Table => Wrap("DROP TABLE IF EXISTS", NameAfter(sql, "TABLE"), " CASCADE"),
            MigrationObjectKind.ForeignTable => Wrap("DROP FOREIGN TABLE IF EXISTS", NameAfter(sql, "TABLE"), " CASCADE"),
            MigrationObjectKind.Type => Wrap("DROP TYPE IF EXISTS", NameAfter(sql, "TYPE"), " CASCADE"),
            MigrationObjectKind.Sequence => Wrap("DROP SEQUENCE IF EXISTS", NameAfter(sql, "SEQUENCE"), " CASCADE"),
            MigrationObjectKind.Domain => Wrap("DROP DOMAIN IF EXISTS", NameAfter(sql, "DOMAIN"), " CASCADE"),
            MigrationObjectKind.Schema => Wrap("DROP SCHEMA IF EXISTS", NameAfter(sql, "SCHEMA"), " CASCADE"),
            MigrationObjectKind.Extension => Wrap("DROP EXTENSION IF EXISTS", NameAfter(sql, "EXTENSION"), " CASCADE"),
            MigrationObjectKind.View => BuildViewDrop(sql),
            MigrationObjectKind.Index => BuildIndexDrop(sql),
            MigrationObjectKind.Constraint => BuildConstraintDrop(sql),
            MigrationObjectKind.Trigger => BuildTriggerDrop(sql),
            MigrationObjectKind.Policy => BuildPolicyDrop(sql),
            MigrationObjectKind.Function => BuildFunctionDrop(sql),
            MigrationObjectKind.Comment => BuildCommentReset(sql),
            MigrationObjectKind.Grant => BuildGrantRevoke(sql),
            _ => null
        };
    }

    private static string? Wrap(string prefix, string? name, string suffix)
        => name is null ? null : $"{prefix} {name}{suffix};";

    private static string? BuildViewDrop(string sql)
    {
        if (ContainsWord(sql, "MATERIALIZED"))
            return Wrap("DROP MATERIALIZED VIEW IF EXISTS", NameAfter(sql, "VIEW"), " CASCADE");

        return Wrap("DROP VIEW IF EXISTS", NameAfter(sql, "VIEW"), " CASCADE");
    }

    private static string? BuildIndexDrop(string sql)
    {
        var name = NameAfter(sql, "INDEX");
        if (name is null)
            return null;

        var table = NameAfter(sql, "ON");
        if (table is not null)
        {
            var dot = table.IndexOf('.');
            if (dot > 0)
            {
                var schema = table[..dot];
                return $"DROP INDEX IF EXISTS {schema}.{name};";
            }
        }

        return $"DROP INDEX IF EXISTS {name};";
    }

    private static string? BuildConstraintDrop(string sql)
    {
        // ALTER TABLE [ONLY] <table> ADD CONSTRAINT <name> ...
        var table = NameAfter(sql, "TABLE");
        var constraint = NameAfter(sql, "CONSTRAINT");
        if (table is null || constraint is null)
            return null;

        return $"ALTER TABLE {table} DROP CONSTRAINT IF EXISTS {constraint};";
    }

    private static string? BuildTriggerDrop(string sql)
    {
        var trigger = NameAfter(sql, "TRIGGER");
        var table = NameAfter(sql, "ON");
        if (trigger is null || table is null)
            return null;

        return $"DROP TRIGGER IF EXISTS {trigger} ON {table};";
    }

    private static string? BuildPolicyDrop(string sql)
    {
        var policy = NameAfter(sql, "POLICY");
        var table = NameAfter(sql, "ON");
        if (policy is null || table is null)
            return null;

        return $"DROP POLICY IF EXISTS {policy} ON {table};";
    }

    private static string? BuildFunctionDrop(string sql)
    {
        // CREATE [OR REPLACE] FUNCTION <schema>.<name>(<args>) RETURNS ...
        var start = IndexAfterWord(sql, "FUNCTION");
        if (start < 0)
            return null;

        var open = sql.IndexOf('(', start);
        if (open < 0)
            return null;

        var close = FindMatchingParen(sql, open);
        if (close < 0)
            return null;

        var name = sql[start..open].Trim();
        var args = sql[(open + 1)..close].Trim();
        var signature = StripArgumentDefaults(args);

        return $"DROP FUNCTION IF EXISTS {name}({signature}) CASCADE;";
    }

    private static string? BuildCommentReset(string sql)
    {
        // COMMENT ON <type> <name> IS '...'  ->  COMMENT ON <type> <name> IS NULL;
        var isIndex = LastIndexOfWord(sql, "IS");
        if (isIndex < 0)
            return null;

        return sql[..isIndex].TrimEnd() + " IS NULL;";
    }

    private static string? BuildGrantRevoke(string sql)
    {
        var trimmed = sql.TrimEnd(';').Trim();
        if (!StartsWithWord(trimmed, "GRANT"))
            return null;

        // GRANT <privs> ON <obj> TO <roles> [WITH GRANT OPTION]
        var toIndex = LastIndexOfWord(trimmed, "TO");
        if (toIndex < 0)
            return null;

        var head = trimmed["GRANT".Length..toIndex];
        var roles = trimmed[(toIndex + "TO".Length)..].Trim();
        roles = StripWord(roles, "WITH GRANT OPTION");

        return $"REVOKE{head}FROM {roles};";
    }

    /// <summary>
    /// Extracts the identifier (possibly schema-qualified and/or quoted) that follows a
    /// given keyword, skipping a leading "IF NOT EXISTS", "ONLY", or "OR REPLACE".
    /// </summary>
    private static string? NameAfter(string sql, string keyword)
    {
        var index = IndexAfterWord(sql, keyword);
        if (index < 0)
            return null;

        var span = sql[index..];
        span = SkipLeadingNoise(span);

        return ReadIdentifier(span);
    }

    private static string SkipLeadingNoise(string text)
    {
        text = text.TrimStart();

        while (true)
        {
            if (StartsWithWord(text, "IF NOT EXISTS"))
                text = text["IF NOT EXISTS".Length..].TrimStart();
            else if (StartsWithWord(text, "ONLY"))
                text = text["ONLY".Length..].TrimStart();
            else
                break;
        }

        return text;
    }

    private static string? ReadIdentifier(string text)
    {
        text = text.TrimStart();
        if (text.Length == 0)
            return null;

        var sb = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"')
            {
                sb.Append('"');
                i++;
                while (i < text.Length)
                {
                    sb.Append(text[i]);
                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    i++;
                }

                // Continue to capture ".next" for schema-qualified names.
                if (i < text.Length && text[i] == '.')
                {
                    sb.Append('.');
                    i++;
                    continue;
                }

                break;
            }

            if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$')
            {
                sb.Append(c);
                i++;
                continue;
            }

            break;
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string StripArgumentDefaults(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "";

        var parts = SplitTopLevelCommas(args);
        var cleaned = new List<string>();

        foreach (var part in parts)
        {
            var p = part.Trim();
            var defaultIndex = IndexOfWord(p, "DEFAULT", 0);
            if (defaultIndex >= 0)
                p = p[..defaultIndex].Trim();

            var eq = p.IndexOf('=');
            if (eq >= 0)
                p = p[..eq].Trim();

            cleaned.Add(p);
        }

        return string.Join(", ", cleaned);
    }

    private static List<string> SplitTopLevelCommas(string text)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        foreach (var c in text)
        {
            if (inSingle)
            {
                sb.Append(c);
                if (c == '\'') inSingle = false;
                continue;
            }

            if (inDouble)
            {
                sb.Append(c);
                if (c == '"') inDouble = false;
                continue;
            }

            switch (c)
            {
                case '\'': inSingle = true; sb.Append(c); break;
                case '"': inDouble = true; sb.Append(c); break;
                case '(': depth++; sb.Append(c); break;
                case ')': depth--; sb.Append(c); break;
                case ',' when depth == 0:
                    parts.Add(sb.ToString());
                    sb.Clear();
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        if (sb.Length > 0)
            parts.Add(sb.ToString());

        return parts;
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

    private static int IndexAfterWord(string text, string word)
    {
        var index = IndexOfWord(text, word, 0);
        return index < 0 ? -1 : index + word.Length;
    }

    private static int IndexOfWord(string text, string word, int start)
    {
        var i = start;
        while (i >= 0 && i <= text.Length - word.Length)
        {
            var found = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (IsWordBoundary(text, found, word))
                return found;

            i = found + 1;
        }

        return -1;
    }

    private static int LastIndexOfWord(string text, string word)
    {
        var i = text.Length - word.Length;
        while (i >= 0)
        {
            var found = text.LastIndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (IsWordBoundary(text, found, word))
                return found;

            i = found - 1;
        }

        return -1;
    }

    private static bool IsWordBoundary(string text, int index, string word)
    {
        if (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_'))
            return false;

        var after = index + word.Length;
        if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
            return false;

        return true;
    }

    private static bool ContainsWord(string text, string word) => IndexOfWord(text, word, 0) >= 0;

    private static bool StartsWithWord(string text, string word)
    {
        text = text.TrimStart();
        return text.Length >= word.Length
            && string.Equals(text[..word.Length], word, StringComparison.OrdinalIgnoreCase)
            && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]));
    }

    private static string StripWord(string text, string word)
    {
        var index = IndexOfWord(text, word, 0);
        return index < 0 ? text.Trim() : text[..index].Trim();
    }
}
