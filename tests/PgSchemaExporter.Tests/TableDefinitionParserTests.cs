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

    [Theory]
    [InlineData("CONSTRAINT pk PRIMARY KEY (\"id\")")]
    [InlineData("UNIQUE (\"email\")")]
    [InlineData("CHECK (\"id\">0)")]
    [InlineData("FOREIGN KEY (\"id\") REFERENCES other(id)")]
    [InlineData("EXCLUDE USING gist (\"id\" WITH =)")]
    [InlineData("LIKE other_table")]
    public void Parse_MarksInlineConstraintKeywordsAsNotParseable(string constraint)
    {
        var sql = $"""
CREATE TABLE "public"."t" (
    "id" integer NOT NULL,
    {constraint}
);
""";

        var table = TableDefinitionParser.Parse(sql);
        Assert.NotNull(table);
        Assert.False(table!.IsParseable);
    }

    [Fact]
    public void Parse_OnlyKeyword_IsIgnored()
    {
        const string sql = """
CREATE TABLE ONLY "public"."t" (
    "id" integer NOT NULL
);
""";

        var table = TableDefinitionParser.Parse(sql);
        Assert.NotNull(table);
        Assert.Equal("\"public\".\"t\"", table!.QualifiedName);
        Assert.True(table.IsParseable);
    }

    [Fact]
    public void Parse_CollateAndGeneratedClauses_AreCaptured()
    {
        const string sql = """
CREATE TABLE "public"."t" (
    "id" integer NOT NULL,
    "name" text COLLATE "en_US" GENERATED ALWAYS AS (upper("name")) STORED
);
""";

        var table = TableDefinitionParser.Parse(sql);
        Assert.NotNull(table);
        Assert.True(table!.IsParseable);

        var name = table.Columns[1];
        Assert.Equal("name", name.Name);
        Assert.Equal("text", name.DataType);
        Assert.Equal("\"en_US\"", name.Collation);
        Assert.NotNull(name.Identity);
        Assert.Contains("GENERATED ALWAYS", name.Identity);
    }

    [Fact]
    public void Parse_NullableAndNotNullClauses_AreHandled()
    {
        const string sql = """
CREATE TABLE "public"."t" (
    "a" integer NOT NULL,
    "b" integer NULL
);
""";

        var table = TableDefinitionParser.Parse(sql);
        Assert.NotNull(table);
        Assert.True(table!.Columns[0].NotNull);
        Assert.False(table.Columns[1].NotNull);
    }

    [Fact]
    public void Parse_EscapedQuotesInIdentifier_AreUnquoted()
    {
        const string sql = """
CREATE TABLE "public"."my""table" (
    "my""id" integer NOT NULL
);
""";

        var table = TableDefinitionParser.Parse(sql);
        Assert.NotNull(table);
        Assert.Equal("\"public\".\"my\"\"table\"", table!.QualifiedName);
        Assert.Equal("my\"id", table.Columns[0].Name);
    }

    [Fact]
    public void Parse_EmptyOrInvalidSql_ReturnsNull()
    {
        Assert.Null(TableDefinitionParser.Parse(""));
        Assert.Null(TableDefinitionParser.Parse("   "));
        Assert.Null(TableDefinitionParser.Parse("SELECT 1;"));
    }

    [Fact]
    public void Parse_MissingTableName_ReturnsNull()
    {
        const string sql = """
CREATE TABLE (
    "id" integer
);
""";

        Assert.Null(TableDefinitionParser.Parse(sql));
    }

    [Fact]
    public void Parse_UnquotedColumnName_ReturnsNotParseable()
    {
        const string sql = """
CREATE TABLE "public"."t" (
    id integer
);
""";

        var table = TableDefinitionParser.Parse(sql);
        Assert.NotNull(table);
        Assert.False(table!.IsParseable);
    }
}
