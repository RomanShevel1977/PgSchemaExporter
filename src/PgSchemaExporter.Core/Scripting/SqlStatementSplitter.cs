using System.Text;

namespace PgSchemaExporter.Core.Scripting;

public sealed class SqlStatementSplitter
{
    private readonly SqlStatementCache _cache = new();

    public IReadOnlyList<string> Split(string sql)
    {
        return _cache.SplitStatements(sql);
    }
}
