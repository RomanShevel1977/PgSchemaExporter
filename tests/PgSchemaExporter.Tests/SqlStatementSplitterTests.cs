using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SqlStatementSplitterTests
{
    private readonly SqlStatementSplitter _splitter = new();

    [Fact]
    public void Split_SimpleStatements_ReturnsTwo()
    {
        var sql = "CREATE TABLE t (id int); CREATE INDEX idx ON t (id);";
        var statements = _splitter.Split(sql);
        Assert.Equal(2, statements.Count);
        Assert.Equal("CREATE TABLE t (id int);", statements[0]);
        Assert.Equal("CREATE INDEX idx ON t (id);", statements[1]);
    }

    [Fact]
    public void Split_StatementWithoutSemicolon_ReturnsTail()
    {
        var sql = "SELECT 1";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
        Assert.Equal("SELECT 1", statements[0]);
    }

    [Fact]
    public void Split_SingleQuoteEscaped_IgnoresSemicolonInside()
    {
        var sql = "INSERT INTO t VALUES ('a; b');";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
        Assert.Equal("INSERT INTO t VALUES ('a; b');", statements[0]);
    }

    [Fact]
    public void Split_DoubleQuoteEscaped_IgnoresSemicolonInside()
    {
        var sql = "SELECT \"a; b\";";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
    }

    [Fact]
    public void Split_DollarQuotedFunctionBody_IgnoresSemicolonInside()
    {
        var sql = "CREATE FUNCTION f() RETURNS int AS $func$ BEGIN RETURN 1; END; $func$ LANGUAGE plpgsql;";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
        Assert.Contains("$func$", statements[0]);
    }

    [Fact]
    public void Split_LineComment_IsIncludedUntilNewLine()
    {
        var sql = "-- comment\nSELECT 1;";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
        Assert.Contains("-- comment", statements[0]);
    }

    [Fact]
    public void Split_BlockComment_IsIncludedUntilClosed()
    {
        var sql = "/* comment ; */ SELECT 1;";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
        Assert.Contains("/* comment ; */", statements[0]);
    }

    [Fact]
    public void Split_OnlyWhitespace_ReturnsEmpty()
    {
        var statements = _splitter.Split("   \n\t  ");
        Assert.Empty(statements);
    }

    [Fact]
    public void Split_NonTerminatedDollarTag_UsesTagAsText()
    {
        var sql = "SELECT $tag$;";
        var statements = _splitter.Split(sql);
        Assert.Single(statements);
    }
}
