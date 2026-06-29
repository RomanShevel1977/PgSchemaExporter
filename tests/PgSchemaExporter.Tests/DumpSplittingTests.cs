using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DumpSplittingTests
{
    [Fact]
    public void Splitter_KeepsDollarQuotedFunctionBodyIntact()
    {
        const string sql = """
            CREATE TABLE public.users (id int);
            CREATE FUNCTION public.f() RETURNS int AS $$ BEGIN RETURN 1; END; $$ LANGUAGE plpgsql;
            """;

        var statements = new SqlStatementSplitter().Split(sql);

        Assert.Equal(2, statements.Count);
        Assert.Contains(statements, s => s.Contains("BEGIN RETURN 1; END;"));
    }

    [Fact]
    public void Splitter_DoesNotSplitOnSemicolonInsideStringLiteral()
    {
        const string sql = "INSERT INTO t VALUES ('a;b'); SELECT 1;";

        var statements = new SqlStatementSplitter().Split(sql);

        Assert.Equal(2, statements.Count);
        Assert.Contains("'a;b'", statements[0]);
    }

    [Theory]
    [InlineData("CREATE TABLE public.users (id int);", SqlObjectType.Table, "public", "users")]
    [InlineData("CREATE MATERIALIZED VIEW public.mv AS SELECT 1;", SqlObjectType.View, "public", "mv")]
    [InlineData("CREATE OR REPLACE VIEW public.v AS SELECT 1;", SqlObjectType.View, "public", "v")]
    [InlineData("CREATE UNLOGGED TABLE app.cache (k text);", SqlObjectType.Table, "app", "cache")]
    public void Classifier_IdentifiesObjectKinds(string statement, SqlObjectType expectedType, string expectedSchema, string expectedName)
    {
        var result = new PgDumpObjectClassifier().Classify(statement, 1);

        Assert.Equal(expectedType, result.Type);
        Assert.Equal(expectedSchema, result.Schema);
        Assert.Equal(expectedName, result.Name);
    }

    [Fact]
    public void Classifier_ParsesConstraintWithParentTable()
    {
        const string statement = "ALTER TABLE ONLY public.users ADD CONSTRAINT users_pkey PRIMARY KEY (id);";

        var result = new PgDumpObjectClassifier().Classify(statement, 5);

        Assert.Equal(SqlObjectType.Constraint, result.Type);
        Assert.Equal("public", result.Schema);
        Assert.Equal("users", result.ParentName);
        Assert.Equal("users_pkey", result.Name);
    }
}
