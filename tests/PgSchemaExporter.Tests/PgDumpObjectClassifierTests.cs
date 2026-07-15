using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class PgDumpObjectClassifierTests
{
    private readonly PgDumpObjectClassifier _classifier = new();

    [Theory]
    [InlineData("CREATE SCHEMA public;", SqlObjectType.Schema, "public", "public")]
    [InlineData("CREATE EXTENSION IF NOT EXISTS pg_trgm;", SqlObjectType.Extension, "public", "pg_trgm")]
    [InlineData("CREATE TYPE public.status AS ENUM ('a');", SqlObjectType.Type, "public", "status")]
    [InlineData("CREATE SEQUENCE public.seq START 1;", SqlObjectType.Sequence, "public", "seq")]
    [InlineData("CREATE UNLOGGED TABLE public.t (id int);", SqlObjectType.Table, "public", "t")]
    [InlineData("CREATE INDEX IF NOT EXISTS idx ON public.t (id);", SqlObjectType.Index, "public", "idx")]
    [InlineData("CREATE INDEX CONCURRENTLY idx ON public.t (id);", SqlObjectType.Index, "public", "idx")]
    [InlineData("CREATE OR REPLACE VIEW public.v AS SELECT 1;", SqlObjectType.View, "public", "v")]
    [InlineData("CREATE MATERIALIZED VIEW public.mv AS SELECT 1;", SqlObjectType.View, "public", "mv")]
    [InlineData("CREATE OR REPLACE FUNCTION public.f() RETURNS int AS $$ SELECT 1; $$ LANGUAGE sql;", SqlObjectType.Function, "public", "f")]
    [InlineData("CREATE TRIGGER trg ON public.t BEFORE INSERT EXECUTE FUNCTION fn();", SqlObjectType.Trigger, "triggers", "trg")]
    [InlineData("CREATE POLICY pol ON public.t FOR SELECT USING (true);", SqlObjectType.Policy, "policies", "pol")]
    [InlineData("COMMENT ON TABLE public.t IS 'note';", SqlObjectType.Comment, "comments", "comment_00001")]
    [InlineData("GRANT SELECT ON public.t TO app;", SqlObjectType.Grant, "grants", "grant_00001")]
    [InlineData("SELECT 1;", SqlObjectType.Other, "misc", "statement_00001")]
    public void Classify_VariousStatements_MapsToExpectedType(string sql, SqlObjectType expectedType, string expectedSchema, string expectedName)
    {
        var obj = _classifier.Classify(sql, 1);

        Assert.Equal(expectedType, obj.Type);
        Assert.Equal(expectedSchema, obj.Schema);
        Assert.Equal(expectedName, obj.Name);
    }

    [Fact]
    public void Classify_AlterTableConstraint_ExtractsSchemaTableAndName()
    {
        var sql = "ALTER TABLE ONLY \"public\".\"users\" ADD CONSTRAINT \"users_pkey\" PRIMARY KEY (id);";
        var obj = _classifier.Classify(sql, 1);

        Assert.Equal(SqlObjectType.Constraint, obj.Type);
        Assert.Equal("public", obj.Schema);
        Assert.Equal("users", obj.ParentName);
        Assert.Equal("users_pkey", obj.Name);
    }

    [Fact]
    public void Classify_QuotedSchemaQualifiedName_CleansQuotes()
    {
        var sql = "CREATE TABLE \"myschema\".\"mytable\" (id int);";
        var obj = _classifier.Classify(sql, 1);

        Assert.Equal(SqlObjectType.Table, obj.Type);
        Assert.Equal("myschema", obj.Schema);
        Assert.Equal("mytable", obj.Name);
    }

    [Fact]
    public void Classify_Index_StoresParentName()
    {
        var sql = "CREATE INDEX idx ON public.t (id);";
        var obj = _classifier.Classify(sql, 1);

        Assert.Equal(SqlObjectType.Index, obj.Type);
        Assert.Equal("public", obj.Schema);
        Assert.Equal("idx", obj.Name);
        Assert.Equal("t", obj.ParentName);
    }

    [Fact]
    public void Classify_NonTerminatedStatement_IsOther()
    {
        var sql = "ALTER SYSTEM SET foo = 'bar'";
        var obj = _classifier.Classify(sql, 42);

        Assert.Equal(SqlObjectType.Other, obj.Type);
        Assert.Equal("statement_00042", obj.Name);
    }

    [Fact]
    public void Classify_PgDumpNoise_IsRemoved()
    {
        var sql = "-- comment\nSET client_min_messages = warning;\nCREATE TABLE public.t (id int);";
        var obj = _classifier.Classify(sql, 1);

        Assert.Equal(SqlObjectType.Table, obj.Type);
        Assert.Equal("public", obj.Schema);
        Assert.Equal("t", obj.Name);
    }
}
