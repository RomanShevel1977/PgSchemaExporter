using PgSchemaExporter.Core.Scripting;

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
        var materialized = SqlTokenizer.FindKeyword(sql, "MATERIALIZED") >= 0;
        return materialized
            ? Wrap("DROP MATERIALIZED VIEW IF EXISTS", NameAfter(sql, "VIEW"), " CASCADE")
            : Wrap("DROP VIEW IF EXISTS", NameAfter(sql, "VIEW"), " CASCADE");
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
        var tokens = SqlTokenizer.Tokenize(sql);
        var afterName = SqlTokenizer.ReadNameAfter(tokens, "FUNCTION", out var name);
        if (name is null)
            return null;

        var open = sql.IndexOf('(', afterName);
        if (open < 0)
            return null;

        var close = SqlTokenizer.FindMatchingParen(sql, open);
        if (close < 0)
            return null;

        var args = sql[(open + 1)..close].Trim();
        var signature = StripArgumentDefaults(args);

        return $"DROP FUNCTION IF EXISTS {name}({signature}) CASCADE;";
    }

    private static string? BuildCommentReset(string sql)
    {
        // COMMENT ON <type> <name> IS '...'  ->  COMMENT ON <type> <name> IS NULL;
        var tokens = SqlTokenizer.Tokenize(sql);
        var isTokenIndex = SqlTokenizer.FindKeyword(tokens, "IS");
        if (isTokenIndex < 0)
            return null;

        var isToken = tokens[isTokenIndex];
        return sql[..isToken.Start].TrimEnd() + " IS NULL;";
    }

    private static string? BuildGrantRevoke(string sql)
    {
        var trimmed = sql.TrimEnd(';').Trim();
        var tokens = SqlTokenizer.Tokenize(trimmed);
        var grantTokenIndex = SqlTokenizer.FindKeyword(tokens, "GRANT");
        if (grantTokenIndex < 0)
            return null;

        // GRANT <privs> ON <obj> TO <roles> [WITH GRANT OPTION]
        var toTokenIndex = SqlTokenizer.FindKeyword(tokens, "TO");
        if (toTokenIndex < 0)
            return null;

        var grantToken = tokens[grantTokenIndex];
        var toToken = tokens[toTokenIndex];
        var withGrantOptionIndex = SqlTokenizer.FindKeyword(tokens, "WITH GRANT OPTION");

        var head = trimmed[(grantToken.Start + grantToken.Length)..toToken.Start].Trim();
        var rolesEnd = withGrantOptionIndex >= 0 ? tokens[withGrantOptionIndex].Start : trimmed.Length;
        var roles = trimmed[(toToken.Start + toToken.Length)..rolesEnd].Trim();

        return $"REVOKE {head} FROM {roles};";
    }

    /// <summary>
    /// Extracts the identifier (possibly schema-qualified and/or quoted) that follows a
    /// given keyword using the shared token-level SQL tokenizer.
    /// </summary>
    private static string? NameAfter(string sql, string keyword)
        => SqlTokenizer.ReadNameAfter(sql, keyword);


    private static string StripArgumentDefaults(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "";

        var parts = SqlTokenizer.SplitTopLevel(args);
        var cleaned = new List<string>();

        foreach (var part in parts)
        {
            var p = part.Trim();
            var defaultIndex = SqlTokenizer.IndexOfWord(p, "DEFAULT", 0);
            if (defaultIndex >= 0)
                p = p[..defaultIndex].Trim();

            var eq = p.IndexOf('=');
            if (eq >= 0)
                p = p[..eq].Trim();

            cleaned.Add(p);
        }

        return string.Join(", ", cleaned);
    }
}
