namespace PgSchemaExporter.Core.Scripting;

/// <summary>
/// Reusable cache for SQL tokenization results.  Centralizes the parsing of SQL
/// batches into statements and the production of normalized comparison keys so
/// that callers such as <see cref="SqlStatementSplitter"/>,
/// <see cref="MigrationGenerator"/>, and future parsers do not repeat the work.
/// </summary>
public sealed class SqlStatementCache
{
    private readonly Dictionary<string, IReadOnlyList<string>> _splitCache = new();
    private readonly Dictionary<string, string> _normalizeCache = new();

    public IReadOnlyList<string> SplitStatements(string sql)
    {
        if (!_splitCache.TryGetValue(sql, out var cached))
            _splitCache[sql] = cached = SqlTokenizer.SplitStatements(sql);

        return cached;
    }

    public string NormalizeStatement(string sql)
    {
        if (!_normalizeCache.TryGetValue(sql, out var cached))
            _normalizeCache[sql] = cached = SqlTokenizer.NormalizeStatement(sql);

        return cached;
    }
}
