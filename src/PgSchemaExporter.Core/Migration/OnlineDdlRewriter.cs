using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Rewrites index create/drop statements into their <c>CONCURRENTLY</c> forms so
/// they acquire only a SHARE UPDATE EXCLUSIVE lock instead of blocking writes.
/// Concurrent statements cannot run inside a transaction block, so the rewritten
/// statements are flagged with <see cref="MigrationStatement.RunsOutsideTransaction"/>.
/// </summary>
public static class OnlineDdlRewriter
{
    /// <summary>
    /// Returns a new list where index create/drop statements are converted to
    /// their concurrent forms. Non-index statements are returned unchanged.
    /// </summary>
    public static List<MigrationStatement> Rewrite(IReadOnlyList<MigrationStatement> statements)
    {
        var result = new List<MigrationStatement>(statements.Count);

        foreach (var statement in statements)
            result.Add(RewriteStatement(statement));

        return result;
    }

    private static MigrationStatement RewriteStatement(MigrationStatement statement)
    {
        if (statement.Kind != MigrationObjectKind.Index)
            return statement;

        var sql = statement.Sql;

        if (TryFindIndexPrefix(sql, out var prefixEnd))
        {
            var rewritten = sql[..prefixEnd].TrimEnd() + " CONCURRENTLY " + sql[prefixEnd..];
            return Concurrent(statement, rewritten);
        }

        // Already concurrent: still ensure it runs outside a transaction.
        if (sql.Contains("CONCURRENTLY", StringComparison.OrdinalIgnoreCase) && !statement.RunsOutsideTransaction)
            return Concurrent(statement, sql);

        return statement;
    }

    private static bool TryFindIndexPrefix(string sql, out int prefixEnd)
    {
        prefixEnd = 0;

        var i = SkipWhitespace(sql, 0);

        if (!TryMatchKeyword(sql, ref i, "CREATE"))
        {
            // DROP INDEX case.
            i = SkipWhitespace(sql, 0);
            if (!TryMatchKeyword(sql, ref i, "DROP"))
                return false;

            if (!TryMatchKeyword(sql, ref i, "INDEX"))
                return false;

            prefixEnd = SkipWhitespace(sql, i);
            if (PeekKeyword(sql, prefixEnd) == "CONCURRENTLY")
                return false;

            return true;
        }

        // CREATE [UNIQUE] INDEX case.
        var hasUnique = TryMatchKeyword(sql, ref i, "UNIQUE");
        _ = hasUnique;

        if (!TryMatchKeyword(sql, ref i, "INDEX"))
            return false;

        prefixEnd = SkipWhitespace(sql, i);
        if (PeekKeyword(sql, prefixEnd) == "CONCURRENTLY")
            return false;

        return true;
    }

    private static bool TryMatchKeyword(string sql, ref int i, string keyword)
    {
        i = SkipWhitespace(sql, i);
        var token = SqlTokenizer.ReadIdentifier(sql, i, out var after, unquote: false);
        if (token is null || !token.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            return false;

        i = after;
        return true;
    }

    private static string? PeekKeyword(string sql, int i)
    {
        i = SkipWhitespace(sql, i);
        if (i >= sql.Length)
            return null;

        var token = SqlTokenizer.ReadIdentifier(sql, i, out _, unquote: false);
        return token;
    }

    private static int SkipWhitespace(string sql, int i)
    {
        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
            i++;
        return i;
    }

    private static MigrationStatement Concurrent(MigrationStatement original, string sql)
        => new(original.Kind, sql, original.IsDestructive, original.Comment, runsOutsideTransaction: true);
}
