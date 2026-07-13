using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationRenderTests
{
    [Fact]
    public void RenderUp_WithLockTimeout_EmitsSetLockTimeout()
    {
        var script = new MigrationScript
        {
            Up = [new(MigrationObjectKind.Table, "ALTER TABLE public.t ADD COLUMN a int;")]
        };

        var sql = script.RenderUp(new MigrationRenderOptions { LockTimeout = "30s" });

        Assert.Contains("SET lock_timeout = '30s';", sql);
        Assert.Contains("BEGIN;", sql);
        Assert.Contains("COMMIT;", sql);
    }

    [Fact]
    public void RenderUp_WithStatementTimeout_EmitsSetStatementTimeout()
    {
        var script = new MigrationScript
        {
            Up = [new(MigrationObjectKind.Table, "ALTER TABLE public.t ADD COLUMN a int;")]
        };

        var sql = script.RenderUp(new MigrationRenderOptions { StatementTimeout = "1min" });

        Assert.Contains("SET statement_timeout = '1min';", sql);
    }

    [Fact]
    public void RenderUp_ConcurrentStatement_RenderedOutsideTransaction()
    {
        var script = new MigrationScript
        {
            Up =
            [
                new(MigrationObjectKind.Table, "CREATE TABLE public.t (id int);"),
                new(MigrationObjectKind.Index, "CREATE INDEX CONCURRENTLY idx ON public.t (id);", runsOutsideTransaction: true)
            ]
        };

        var sql = script.RenderUp(MigrationRenderOptions.Default);

        // Concurrent statement must appear AFTER COMMIT (outside the transaction).
        var commitIndex = sql.IndexOf("COMMIT;", System.StringComparison.Ordinal);
        var concurrentIndex = sql.IndexOf("CREATE INDEX CONCURRENTLY", System.StringComparison.Ordinal);
        Assert.True(commitIndex >= 0 && concurrentIndex > commitIndex);
        Assert.Contains("run OUTSIDE a transaction", sql);
    }

    [Fact]
    public void RenderDown_ConcurrentStatement_RenderedBeforeTransaction()
    {
        var script = new MigrationScript
        {
            Down =
            [
                new(MigrationObjectKind.Index, "DROP INDEX CONCURRENTLY idx;", runsOutsideTransaction: true),
                new(MigrationObjectKind.Table, "DROP TABLE public.t;", isDestructive: true)
            ]
        };

        var sql = script.RenderDown(MigrationRenderOptions.Default);

        var beginIndex = sql.IndexOf("BEGIN;", System.StringComparison.Ordinal);
        var concurrentIndex = sql.IndexOf("DROP INDEX CONCURRENTLY", System.StringComparison.Ordinal);
        Assert.True(concurrentIndex >= 0 && beginIndex > concurrentIndex);
    }

    [Fact]
    public void RenderUp_SafeMode_CommentsOutDestructive()
    {
        var script = new MigrationScript
        {
            Up = [new(MigrationObjectKind.Table, "DROP TABLE public.t;", isDestructive: true)]
        };

        var sql = script.RenderUp(new MigrationRenderOptions { Safe = true });

        Assert.Contains("-- DESTRUCTIVE", sql);
        Assert.Contains("-- DROP TABLE public.t;", sql);
    }

    [Fact]
    public void RenderUp_BackwardCompatibleBoolOverload_Works()
    {
        var script = new MigrationScript
        {
            Up = [new(MigrationObjectKind.Table, "CREATE TABLE public.t (id int);")]
        };

        var sql = script.RenderUp(safe: false);

        Assert.Contains("CREATE TABLE public.t", sql);
        Assert.Contains("BEGIN;", sql);
    }

    [Fact]
    public void HasConcurrentStatements_TrueWhenFlagged()
    {
        var script = new MigrationScript
        {
            Up = [new(MigrationObjectKind.Index, "CREATE INDEX CONCURRENTLY idx ON public.t (id);", runsOutsideTransaction: true)]
        };

        Assert.True(script.HasConcurrentStatements);
    }
}
