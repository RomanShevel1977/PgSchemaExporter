using System.Text.RegularExpressions;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Rewrites index create/drop statements into their <c>CONCURRENTLY</c> forms so
/// they acquire only a SHARE UPDATE EXCLUSIVE lock instead of blocking writes.
/// Concurrent statements cannot run inside a transaction block, so the rewritten
/// statements are flagged with <see cref="MigrationStatement.RunsOutsideTransaction"/>.
/// </summary>
public static partial class OnlineDdlRewriter
{
    [GeneratedRegex(@"^\s*CREATE\s+(UNIQUE\s+)?INDEX\s+(CONCURRENTLY\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CreateIndexRegex();

    [GeneratedRegex(@"^\s*DROP\s+INDEX\s+(CONCURRENTLY\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropIndexRegex();

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

        var createMatch = CreateIndexRegex().Match(sql);
        if (createMatch.Success && !createMatch.Groups[2].Success)
        {
            // Rewrite only the matched prefix, leaving the rest of the statement intact.
            var rewritten = sql[..createMatch.Length].TrimEnd() + " CONCURRENTLY " + sql[createMatch.Length..];
            return Concurrent(statement, rewritten);
        }

        var dropMatch = DropIndexRegex().Match(sql);
        if (dropMatch.Success && !dropMatch.Groups[1].Success)
        {
            var rewritten = sql[..dropMatch.Length].TrimEnd() + " CONCURRENTLY " + sql[dropMatch.Length..];
            return Concurrent(statement, rewritten);
        }

        // Already concurrent: still ensure it runs outside a transaction.
        if (sql.Contains("CONCURRENTLY", StringComparison.OrdinalIgnoreCase) && !statement.RunsOutsideTransaction)
            return Concurrent(statement, sql);

        return statement;
    }

    private static MigrationStatement Concurrent(MigrationStatement original, string sql)
        => new(original.Kind, sql, original.IsDestructive, original.Comment, runsOutsideTransaction: true);
}
