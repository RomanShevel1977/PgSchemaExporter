using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaDifferTests : IDisposable
{
    private readonly string _root;
    private readonly string _left;
    private readonly string _right;

    public SchemaDifferTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-diff-" + Guid.NewGuid().ToString("n"));
        _left = Path.Combine(_root, "left");
        _right = Path.Combine(_root, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
    }

    [Fact]
    public void Diff_DetectsAddedRemovedChangedAndUnchanged()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);");

        WriteFile(_left, "tables/public.orders.sql", "CREATE TABLE orders (id int);");
        WriteFile(_right, "tables/public.orders.sql", "CREATE TABLE orders (id bigint);");

        WriteFile(_left, "views/public.old_view.sql", "SELECT 1;");

        WriteFile(_right, "views/public.new_view.sql", "SELECT 2;");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        Assert.Equal(["views/public.new_view.sql"], result.Added);
        Assert.Equal(["views/public.old_view.sql"], result.Removed);
        Assert.Equal(["tables/public.orders.sql"], result.Changed);
        Assert.Equal(["tables/public.users.sql"], result.Unchanged);
        Assert.True(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoresLineEndingDifferences()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);\nCOMMIT;");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);\r\nCOMMIT;\r\n");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        Assert.False(result.HasDifferences);
        Assert.Single(result.Unchanged);
    }

    private static void WriteFile(string root, string relativePath, string content)
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
