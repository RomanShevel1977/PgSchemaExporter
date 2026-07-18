using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SqlTokenizerTests
{
    [Fact]
    public void Tokenize_SimpleSelect_ProducesExpectedTokens()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT id FROM public.users;");

        Assert.Equal(10, tokens.Count);
        Assert.Equal(SqlTokenKind.Word, tokens[0].Kind);
        Assert.Equal("SELECT", tokens[0].Text);
        Assert.Equal(SqlTokenKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SqlTokenKind.Word, tokens[2].Kind);
        Assert.Equal("id", tokens[2].Text);
        Assert.Equal(SqlTokenKind.Whitespace, tokens[3].Kind);
        Assert.Equal(SqlTokenKind.Word, tokens[4].Kind);
        Assert.Equal("FROM", tokens[4].Text);
        Assert.Equal(SqlTokenKind.Whitespace, tokens[5].Kind);
        Assert.Equal(SqlTokenKind.Word, tokens[6].Kind);
        Assert.Equal("public", tokens[6].Text);
        Assert.Equal(SqlTokenKind.Symbol, tokens[7].Kind);
        Assert.Equal(".", tokens[7].Text);
        Assert.Equal(SqlTokenKind.Word, tokens[8].Kind);
        Assert.Equal("users", tokens[8].Text);
        Assert.Equal(SqlTokenKind.Symbol, tokens[9].Kind);
        Assert.Equal(";", tokens[9].Text);
    }

    [Fact]
    public void Tokenize_StringLiteral_PreservesContent()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT 'hello ''world''';");

        var literal = tokens[2];
        Assert.Equal(SqlTokenKind.StringLiteral, literal.Kind);
        Assert.Equal("'hello ''world'''", literal.Text);
    }

    [Fact]
    public void Tokenize_QuotedIdentifier_PreservesQuotes()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT \"my\"\"column\" FROM t;");

        var identifier = tokens[2];
        Assert.Equal(SqlTokenKind.QuotedIdentifier, identifier.Kind);
        Assert.Equal("\"my\"\"column\"", identifier.Text);
    }

    [Fact]
    public void Tokenize_DollarQuotedBlock_PreservesTags()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT $func$ begin return 1; end; $func$;");

        var dollar = tokens[2];
        Assert.Equal(SqlTokenKind.DollarQuoted, dollar.Kind);
        Assert.Equal("$func$ begin return 1; end; $func$", dollar.Text);
    }

    [Fact]
    public void Tokenize_LineComment_Isolated()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT 1 -- comment\nFROM t");

        Assert.Contains(tokens, t => t.Kind == SqlTokenKind.LineComment);
    }

    [Fact]
    public void Tokenize_BlockComment_Isolated()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT /* comment */ 1");

        Assert.Contains(tokens, t => t.Kind == SqlTokenKind.BlockComment);
    }

    [Fact]
    public void FindKeyword_ReturnsTokenStart()
    {
        var tokens = SqlTokenizer.Tokenize("CREATE TABLE IF NOT EXISTS public.t (id int);");

        var create = SqlTokenizer.FindKeyword(tokens, "CREATE");
        var table = SqlTokenizer.FindKeyword(tokens, "TABLE");
        var ifNotExists = SqlTokenizer.FindKeyword(tokens, "IF NOT EXISTS");

        Assert.Equal(0, create);
        Assert.Equal(2, table);
        Assert.Equal(4, ifNotExists);
    }

    [Fact]
    public void FindKeyword_IgnoresCommentsAndQuotes()
    {
        var tokens = SqlTokenizer.Tokenize("/* not this */ CREATE -- comment\nTABLE t");

        var table = SqlTokenizer.FindKeyword(tokens, "TABLE");
        Assert.Equal(6, table);
    }

    [Fact]
    public void ReadNameAfter_SkipsNoiseAndReadsQualifiedName()
    {
        const string sql = "CREATE TABLE IF NOT EXISTS \"public\".\"my table\" (id int);";

        var name = SqlTokenizer.ReadNameAfter(sql, "TABLE");

        Assert.Equal("\"public\".\"my table\"", name);
    }

    [Fact]
    public void ReadNameAfter_OnlyKeyword_IsIgnored()
    {
        const string sql = "CREATE TABLE ONLY public.t (id int);";

        var name = SqlTokenizer.ReadNameAfter(sql, "TABLE");

        Assert.Equal("public.t", name);
    }

    [Fact]
    public void ReadParenthesized_ReturnsInnerContent()
    {
        var tokens = SqlTokenizer.Tokenize("PRIMARY KEY (id, tenant_id)");
        var pk = SqlTokenizer.FindKeyword(tokens, "PRIMARY KEY");

        var inner = SqlTokenizer.ReadParenthesized(tokens, "PRIMARY KEY (id, tenant_id)", pk, out _);

        Assert.Equal("id, tenant_id", inner);
    }

    [Fact]
    public void ReadIdentifier_ReadsQuotedSchemaQualifiedName()
    {
        var tokens = SqlTokenizer.Tokenize("\"a\".\"b\"");

        var name = SqlTokenizer.ReadIdentifier(tokens, 0, out var after);

        Assert.Equal("\"a\".\"b\"", name);
        Assert.Equal(tokens.Count, after);
    }
}
