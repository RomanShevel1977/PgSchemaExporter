using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationGeneratorTests : IDisposable
{
    private readonly string _root;
    private readonly string _from;
    private readonly string _to;

    public MigrationGeneratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-migrate-" + Guid.NewGuid().ToString("n"));
        _from = Path.Combine(_root, "from");
        _to = Path.Combine(_root, "to");
        Directory.CreateDirectory(_from);
        Directory.CreateDirectory(_to);
    }

    [Fact]
    public void Generate_NoChanges_ProducesEmptyScript()
    {
        Write(_from, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));
        Write(_to, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));

        var script = Generate();

        Assert.False(script.HasChanges);
        Assert.Empty(script.Up);
    }

    [Fact]
    public void Generate_AddedTable_CreatesUpAndDropsOnDown()
    {
        Write(_to, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql.Contains("CREATE TABLE") && s.Sql.Contains("\"users\""));
        Assert.Contains(script.Down, s => s.Sql.StartsWith("DROP TABLE IF EXISTS") && s.IsDestructive);
    }

    [Fact]
    public void Generate_RemovedTable_DropsOnUpAndRecreatesOnDown()
    {
        Write(_from, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql.StartsWith("DROP TABLE IF EXISTS") && s.IsDestructive);
        Assert.Contains(script.Down, s => s.Sql.Contains("CREATE TABLE"));
    }

    [Fact]
    public void Generate_AddedColumn_EmitsAddColumnAndDropColumn()
    {
        Write(_from, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));
        Write(_to, "tables/public.users.sql", Table("users",
            "\"id\" integer NOT NULL",
            "\"age\" integer DEFAULT 0"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql == "ALTER TABLE \"public\".\"users\" ADD COLUMN \"age\" integer DEFAULT 0;");
        Assert.Contains(script.Down, s => s.Sql == "ALTER TABLE \"public\".\"users\" DROP COLUMN IF EXISTS \"age\";" && s.IsDestructive);
    }

    [Fact]
    public void Generate_DroppedColumn_EmitsDropColumnAndAddColumn()
    {
        Write(_from, "tables/public.users.sql", Table("users",
            "\"id\" integer NOT NULL",
            "\"nickname\" text"));
        Write(_to, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql == "ALTER TABLE \"public\".\"users\" DROP COLUMN IF EXISTS \"nickname\";" && s.IsDestructive);
        Assert.Contains(script.Down, s => s.Sql == "ALTER TABLE \"public\".\"users\" ADD COLUMN \"nickname\" text;");
    }

    [Fact]
    public void Generate_TypeChange_EmitsAlterColumnType()
    {
        Write(_from, "tables/public.users.sql", Table("users", "\"id\" integer NOT NULL"));
        Write(_to, "tables/public.users.sql", Table("users", "\"id\" bigint NOT NULL"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql == "ALTER TABLE \"public\".\"users\" ALTER COLUMN \"id\" TYPE bigint;" && s.IsDestructive);
        Assert.Contains(script.Down, s => s.Sql == "ALTER TABLE \"public\".\"users\" ALTER COLUMN \"id\" TYPE integer;");
    }

    [Fact]
    public void Generate_NullabilityChange_EmitsSetAndDropNotNull()
    {
        Write(_from, "tables/public.users.sql", Table("users", "\"email\" text"));
        Write(_to, "tables/public.users.sql", Table("users", "\"email\" text NOT NULL"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql == "ALTER TABLE \"public\".\"users\" ALTER COLUMN \"email\" SET NOT NULL;");
        Assert.Contains(script.Down, s => s.Sql == "ALTER TABLE \"public\".\"users\" ALTER COLUMN \"email\" DROP NOT NULL;");
    }

    [Fact]
    public void Generate_DefaultChange_EmitsSetAndRestoreDefault()
    {
        Write(_from, "tables/public.users.sql", Table("users", "\"status\" text DEFAULT 'new'"));
        Write(_to, "tables/public.users.sql", Table("users", "\"status\" text DEFAULT 'active'"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql == "ALTER TABLE \"public\".\"users\" ALTER COLUMN \"status\" SET DEFAULT 'active';");
        Assert.Contains(script.Down, s => s.Sql == "ALTER TABLE \"public\".\"users\" ALTER COLUMN \"status\" SET DEFAULT 'new';");
    }

    [Fact]
    public void Generate_ChangedView_UsesReplaceStrategy()
    {
        Write(_from, "views/public.active.sql", "CREATE OR REPLACE VIEW \"public\".\"active\" AS SELECT 1;");
        Write(_to, "views/public.active.sql", "CREATE OR REPLACE VIEW \"public\".\"active\" AS SELECT 2;");

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql.Contains("SELECT 2"));
        Assert.Contains(script.Down, s => s.Sql.Contains("SELECT 1"));
        Assert.DoesNotContain(script.Up, s => s.Sql.StartsWith("DROP VIEW"));
    }

    [Fact]
    public void Generate_AddedIndex_EmitsCreateAndDrop()
    {
        Write(_to, "indexes/public.users.indexes.sql",
            "CREATE INDEX IF NOT EXISTS users_email_idx ON public.users USING btree (email);");

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql.Contains("CREATE INDEX"));
        Assert.Contains(script.Down, s => s.Sql == "DROP INDEX IF EXISTS users_email_idx;");
    }

    [Fact]
    public void Generate_ChangedConstraintsFile_DiffsStatements()
    {
        Write(_from, "constraints/public.users.constraints.sql",
            "ALTER TABLE ONLY \"public\".\"users\" ADD CONSTRAINT \"users_pkey\" PRIMARY KEY (id);");
        Write(_to, "constraints/public.users.constraints.sql",
            "ALTER TABLE ONLY \"public\".\"users\" ADD CONSTRAINT \"users_pkey\" PRIMARY KEY (id);\n" +
            "ALTER TABLE ONLY \"public\".\"users\" ADD CONSTRAINT \"users_email_key\" UNIQUE (email);");

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql.Contains("ADD CONSTRAINT \"users_email_key\""));
        Assert.Contains(script.Down, s => s.Sql == "ALTER TABLE \"public\".\"users\" DROP CONSTRAINT IF EXISTS \"users_email_key\";");
        Assert.DoesNotContain(script.Up, s => s.Sql.Contains("users_pkey"));
    }

    [Fact]
    public void Generate_UnparseableTableChange_FallsBackToRecreate()
    {
        Write(_from, "tables/public.t.sql", Table("t", "\"id\" integer NOT NULL", "PRIMARY KEY (\"id\")"));
        Write(_to, "tables/public.t.sql", Table("t", "\"id\" bigint NOT NULL", "PRIMARY KEY (\"id\")"));

        var script = Generate();

        Assert.Contains(script.Up, s => s.Sql.StartsWith("DROP TABLE IF EXISTS") && s.IsDestructive);
        Assert.Contains(script.Up, s => s.Sql.Contains("CREATE TABLE"));
    }

    private MigrationScript Generate()
        => new MigrationGenerator().Generate(new MigrationOptions
        {
            FromDirectory = _from,
            ToDirectory = _to,
            Preview = true
        });

    private static string Table(string name, params string[] columns)
    {
        var body = string.Join(",\n    ", columns);
        return $"CREATE TABLE IF NOT EXISTS \"public\".\"{name}\" (\n    {body}\n);\n";
    }

    private static void Write(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
