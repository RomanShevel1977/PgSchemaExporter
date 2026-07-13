using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class OnlineDdlRewriterTests
{
    [Fact]
    public void Rewrite_CreateIndex_AddsConcurrentlyAndFlags()
    {
        var input = new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "CREATE INDEX idx_users_email ON public.users (email);")
        };

        var result = OnlineDdlRewriter.Rewrite(input);

        var stmt = Assert.Single(result);
        Assert.Contains("CREATE INDEX CONCURRENTLY", stmt.Sql);
        Assert.Contains("idx_users_email", stmt.Sql);
        Assert.True(stmt.RunsOutsideTransaction);
    }

    [Fact]
    public void Rewrite_CreateUniqueIndex_AddsConcurrently()
    {
        var input = new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "CREATE UNIQUE INDEX idx_u ON public.t (a);")
        };

        var result = OnlineDdlRewriter.Rewrite(input);

        Assert.Contains("CREATE UNIQUE INDEX CONCURRENTLY", result[0].Sql);
        Assert.True(result[0].RunsOutsideTransaction);
    }

    [Fact]
    public void Rewrite_DropIndex_AddsConcurrently()
    {
        var input = new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "DROP INDEX IF EXISTS public.idx_users_email;")
        };

        var result = OnlineDdlRewriter.Rewrite(input);

        Assert.Contains("DROP INDEX CONCURRENTLY", result[0].Sql);
        Assert.True(result[0].RunsOutsideTransaction);
    }

    [Fact]
    public void Rewrite_AlreadyConcurrent_IsNotDoubledButFlagged()
    {
        var input = new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "CREATE INDEX CONCURRENTLY idx ON public.t (a);")
        };

        var result = OnlineDdlRewriter.Rewrite(input);

        // Only one CONCURRENTLY keyword.
        var count = System.Text.RegularExpressions.Regex.Matches(result[0].Sql, "CONCURRENTLY").Count;
        Assert.Equal(1, count);
        Assert.True(result[0].RunsOutsideTransaction);
    }

    [Fact]
    public void Rewrite_NonIndexStatements_Unchanged()
    {
        var input = new List<MigrationStatement>
        {
            new(MigrationObjectKind.Table, "CREATE TABLE public.t (id int);")
        };

        var result = OnlineDdlRewriter.Rewrite(input);

        Assert.Equal(input[0].Sql, result[0].Sql);
        Assert.False(result[0].RunsOutsideTransaction);
    }

    [Fact]
    public void Rewrite_PartialIndex_PreservesWhereClause()
    {
        var input = new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "CREATE INDEX idx ON public.t (a) WHERE status = 'active';")
        };

        var result = OnlineDdlRewriter.Rewrite(input);

        Assert.Contains("CREATE INDEX CONCURRENTLY", result[0].Sql);
        Assert.Contains("WHERE status = 'active'", result[0].Sql);
    }
}
