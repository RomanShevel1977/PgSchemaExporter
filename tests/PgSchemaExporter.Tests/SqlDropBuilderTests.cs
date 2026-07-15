using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SqlDropBuilderTests
{
    [Theory]
    [InlineData(MigrationObjectKind.Table, "CREATE TABLE public.users (id int);", "DROP TABLE IF EXISTS public.users CASCADE;")]
    [InlineData(MigrationObjectKind.ForeignTable, "CREATE FOREIGN TABLE public.ft (id int);", "DROP FOREIGN TABLE IF EXISTS public.ft CASCADE;")]
    [InlineData(MigrationObjectKind.Type, "CREATE TYPE public.status AS ENUM ('a', 'b');", "DROP TYPE IF EXISTS public.status CASCADE;")]
    [InlineData(MigrationObjectKind.Sequence, "CREATE SEQUENCE public.seq START 1;", "DROP SEQUENCE IF EXISTS public.seq CASCADE;")]
    [InlineData(MigrationObjectKind.Domain, "CREATE DOMAIN public.email AS text CHECK (VALUE ~ '@');", "DROP DOMAIN IF EXISTS public.email CASCADE;")]
    [InlineData(MigrationObjectKind.Schema, "CREATE SCHEMA public;", "DROP SCHEMA IF EXISTS public CASCADE;")]
    [InlineData(MigrationObjectKind.Extension, "CREATE EXTENSION pg_trgm;", "DROP EXTENSION IF EXISTS pg_trgm CASCADE;")]
    public void BuildDrop_SimpleObjects_ReturnsExpectedDrop(MigrationObjectKind kind, string create, string expected)
    {
        var drop = SqlDropBuilder.BuildDrop(kind, create);
        Assert.Equal(expected, drop);
    }

    [Fact]
    public void BuildDrop_View_ReturnsDropView()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.View, "CREATE VIEW public.v AS SELECT 1;");
        Assert.Equal("DROP VIEW IF EXISTS public.v CASCADE;", drop);
    }

    [Fact]
    public void BuildDrop_MaterializedView_ReturnsDropMaterializedView()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.View, "CREATE MATERIALIZED VIEW public.mv AS SELECT 1;");
        Assert.Equal("DROP MATERIALIZED VIEW IF EXISTS public.mv CASCADE;", drop);
    }

    [Fact]
    public void BuildDrop_Index_WithSchemaQualifiedTable_ReturnsSchemaQualifiedDrop()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Index, "CREATE INDEX idx ON public.users (id);");
        Assert.Equal("DROP INDEX IF EXISTS public.idx;", drop);
    }

    [Fact]
    public void BuildDrop_Index_WithoutSchema_ReturnsSimpleDrop()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Index, "CREATE INDEX idx ON users (id);");
        Assert.Equal("DROP INDEX IF EXISTS idx;", drop);
    }

    [Fact]
    public void BuildDrop_Constraint_ReturnsAlterTableDropConstraint()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Constraint, "ALTER TABLE public.users ADD CONSTRAINT pk_users PRIMARY KEY (id);");
        Assert.Equal("ALTER TABLE public.users DROP CONSTRAINT IF EXISTS pk_users;", drop);
    }

    [Fact]
    public void BuildDrop_Trigger_ReturnsDropTrigger()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Trigger, "CREATE TRIGGER trg ON public.users BEFORE INSERT EXECUTE FUNCTION fn();");
        Assert.Equal("DROP TRIGGER IF EXISTS trg ON public.users;", drop);
    }

    [Fact]
    public void BuildDrop_Policy_ReturnsDropPolicy()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Policy, "CREATE POLICY pol ON public.users FOR SELECT TO public USING (true);");
        Assert.Equal("DROP POLICY IF EXISTS pol ON public.users;", drop);
    }

    [Fact]
    public void BuildDrop_Function_WithDefaults_StripsDefaults()
    {
        var create = "CREATE FUNCTION public.add(a int DEFAULT 1, b int = 2) RETURNS int AS $$ SELECT a + b; $$ LANGUAGE sql;";
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Function, create);
        Assert.Equal("DROP FUNCTION IF EXISTS public.add(a int, b int) CASCADE;", drop);
    }

    [Fact]
    public void BuildDrop_Comment_ReturnsCommentIsNull()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Comment, "COMMENT ON TABLE public.users IS 'note';");
        Assert.Equal("COMMENT ON TABLE public.users IS NULL;", drop);
    }

    [Fact]
    public void BuildDrop_Grant_ReturnsRevoke()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Grant, "GRANT SELECT ON TABLE public.users TO app_user WITH GRANT OPTION;");
        Assert.Equal("REVOKE SELECT ON TABLE public.users FROM app_user;", drop);
    }

    [Fact]
    public void BuildDrops_ReversesOrder()
    {
        var drops = SqlDropBuilder.BuildDrops(MigrationObjectKind.Table, ["CREATE TABLE public.a (id int);", "CREATE TABLE public.b (id int);"]);
        Assert.Equal("DROP TABLE IF EXISTS public.b CASCADE;", drops[0]);
        Assert.Equal("DROP TABLE IF EXISTS public.a CASCADE;", drops[1]);
    }

    [Fact]
    public void BuildDrop_UnknownKind_ReturnsNull()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Unknown, "CREATE TABLE public.t (id int);");
        Assert.Null(drop);
    }

    [Fact]
    public void BuildDrop_Function_MissingParens_ReturnsNull()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Function, "CREATE FUNCTION public.f RETURNS int;");
        Assert.Null(drop);
    }

    [Fact]
    public void BuildDrop_QuotedIdentifier_WithSchema_ReturnsQuotedName()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Table, "CREATE TABLE \"my schema\".\"my table\" (id int);");
        Assert.Equal("DROP TABLE IF EXISTS \"my schema\".\"my table\" CASCADE;", drop);
    }

    [Fact]
    public void BuildDrop_Table_IfNotExists_IsIgnored()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Table, "CREATE TABLE IF NOT EXISTS public.t (id int);");
        Assert.Equal("DROP TABLE IF EXISTS public.t CASCADE;", drop);
    }

    [Fact]
    public void BuildDrop_Table_Only_IsIgnored()
    {
        var drop = SqlDropBuilder.BuildDrop(MigrationObjectKind.Table, "CREATE TABLE ONLY public.t (id int);");
        Assert.Equal("DROP TABLE IF EXISTS public.t CASCADE;", drop);
    }

    [Fact]
    public void FromRelativePath_MapsAllKnownFolders()
    {
        Assert.Equal(MigrationObjectKind.Schema, MigrationObjectKinds.FromRelativePath("schemas/public.sql"));
        Assert.Equal(MigrationObjectKind.Extension, MigrationObjectKinds.FromRelativePath("extensions/pg_trgm.sql"));
        Assert.Equal(MigrationObjectKind.Type, MigrationObjectKinds.FromRelativePath("types/status.sql"));
        Assert.Equal(MigrationObjectKind.Sequence, MigrationObjectKinds.FromRelativePath("sequences/seq.sql"));
        Assert.Equal(MigrationObjectKind.Domain, MigrationObjectKinds.FromRelativePath("domains/email.sql"));
        Assert.Equal(MigrationObjectKind.Table, MigrationObjectKinds.FromRelativePath("tables/public/users.sql"));
        Assert.Equal(MigrationObjectKind.ForeignTable, MigrationObjectKinds.FromRelativePath("foreign_tables/public/ft.sql"));
        Assert.Equal(MigrationObjectKind.Constraint, MigrationObjectKinds.FromRelativePath("constraints/public/users/pk.sql"));
        Assert.Equal(MigrationObjectKind.Index, MigrationObjectKinds.FromRelativePath("indexes/public/users/idx.sql"));
        Assert.Equal(MigrationObjectKind.View, MigrationObjectKinds.FromRelativePath("views/public/v.sql"));
        Assert.Equal(MigrationObjectKind.Function, MigrationObjectKinds.FromRelativePath("functions/public/add.sql"));
        Assert.Equal(MigrationObjectKind.Trigger, MigrationObjectKinds.FromRelativePath("triggers/public/users/trg.sql"));
        Assert.Equal(MigrationObjectKind.Policy, MigrationObjectKinds.FromRelativePath("policies/public/users/pol.sql"));
        Assert.Equal(MigrationObjectKind.Comment, MigrationObjectKinds.FromRelativePath("comments/public/users.sql"));
        Assert.Equal(MigrationObjectKind.Grant, MigrationObjectKinds.FromRelativePath("grants/public/users.sql"));
    }

    [Fact]
    public void FromRelativePath_UnknownFolder_ReturnsUnknown()
    {
        Assert.Equal(MigrationObjectKind.Unknown, MigrationObjectKinds.FromRelativePath("unknown/file.sql"));
    }
}
