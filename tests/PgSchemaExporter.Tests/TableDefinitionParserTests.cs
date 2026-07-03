using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class TableDefinitionParserTests
{
    [Fact]
    public void Parse_ReadsQualifiedNameAndColumns()
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS "public"."users" (
    "id" integer NOT NULL,
    "email" character varying(255),
    "balance" numeric(10,2) DEFAULT 0
);
""";

        var table = TableDefinitionParser.Parse(sql);

        Assert.NotNull(table);
        Assert.True(table!.IsParseable);
        Assert.Equal("\"public\".\"users\"", table.QualifiedName);
        Assert.Equal(3, table.Columns.Count);

        var id = table.Columns[0];
        Assert.Equal("id", id.Name);
        Assert.Equal("integer", id.DataType);
        Assert.True(id.NotNull);

        var email = table.Columns[1];
        Assert.Equal("email", email.Name);
        Assert.Equal("character varying(255)", email.DataType);
        Assert.False(email.NotNull);

        var balance = table.Columns[2];
        Assert.Equal("numeric(10,2)", balance.DataType);
        Assert.Equal("0", balance.Default);
    }

    [Fact]
    public void Parse_HandlesDefaultWithCommasAndParens()
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS "public"."t" (
    "id" integer DEFAULT nextval('public.t_id_seq'::regclass) NOT NULL,
    "name" text COLLATE "en_US"
);
""";

        var table = TableDefinitionParser.Parse(sql);

        Assert.NotNull(table);
        var id = table!.Columns[0];
        Assert.Equal("integer", id.DataType);
        Assert.Equal("nextval('public.t_id_seq'::regclass)", id.Default);
        Assert.True(id.NotNull);

        var name = table.Columns[1];
        Assert.Equal("text", name.DataType);
        Assert.Equal("\"en_US\"", name.Collation);
    }

    [Fact]
    public void Parse_MarksInlineConstraintsAsNotParseable()
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS "public"."t" (
    "id" integer NOT NULL,
    PRIMARY KEY ("id")
);
""";

        var table = TableDefinitionParser.Parse(sql);

        Assert.NotNull(table);
        Assert.False(table!.IsParseable);
    }
}
