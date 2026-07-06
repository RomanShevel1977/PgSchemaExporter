using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;
using System.Text.Json;
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

    [Fact]
    public void DiffOptions_RequiresLeftOrLeftDb()
    {
        var options = new SchemaDiffOptions
        {
            RightDirectory = _right
        };

        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Fact]
    public void DiffOptions_RequiresRightOrRightDb()
    {
        var options = new SchemaDiffOptions
        {
            LeftDirectory = _left
        };

        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Fact]
    public void DiffOptions_AcceptsLeftDb()
    {
        var options = new SchemaDiffOptions
        {
            LeftConnectionString = "Host=localhost;Database=test",
            RightDirectory = _right
        };

        options.EnsureValid();
    }

    [Fact]
    public void DiffOptions_AcceptsRightDb()
    {
        var options = new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightConnectionString = "Host=localhost;Database=test"
        };

        options.EnsureValid();
    }

    [Fact]
    public void DiffOptions_AcceptsBothDb()
    {
        var options = new SchemaDiffOptions
        {
            LeftConnectionString = "Host=localhost;Database=test1",
            RightConnectionString = "Host=localhost;Database=test2"
        };

        options.EnsureValid();
    }

    [Fact]
    public void DiffReportWriter_GeneratesJsonOutput()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);");

        WriteFile(_right, "tables/public.orders.sql", "CREATE TABLE orders (id int);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        var writer = new SchemaDiffReportWriter();
        var json = writer.BuildReport(result, DiffFormat.Json);

        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(parsed.TryGetProperty("added", out var added));
        Assert.Equal(1, added.GetArrayLength());
        Assert.Equal("tables/public.orders.sql", added[0].GetString());

        Assert.True(parsed.TryGetProperty("removed", out var removed));
        Assert.Equal(0, removed.GetArrayLength());

        Assert.True(parsed.TryGetProperty("changed", out var changed));
        Assert.Equal(0, changed.GetArrayLength());

        Assert.True(parsed.TryGetProperty("unchanged", out var unchanged));
        Assert.Equal(1, unchanged.GetArrayLength());

        Assert.True(parsed.TryGetProperty("hasDifferences", out var hasDifferences));
        Assert.True(hasDifferences.GetBoolean());
    }

    [Fact]
    public void DiffReportWriter_GeneratesTextOutput()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        var writer = new SchemaDiffReportWriter();
        var text = writer.BuildReport(result, DiffFormat.Text);

        Assert.Contains("# Schema diff report", text);
        Assert.Contains("Added:     0", text);
        Assert.Contains("Removed:   0", text);
        Assert.Contains("Changed:   0", text);
        Assert.Contains("Unchanged: 1", text);
    }

    [Fact]
    public void DiffReportWriter_GeneratesHtmlOutput()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.orders.sql", "CREATE TABLE orders (id int);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        var writer = new SchemaDiffReportWriter();
        var html = writer.BuildReport(result, DiffFormat.Html);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Schema diff report", html);
        Assert.Contains("tables/public.orders.sql", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void DiffReportWriter_HtmlEncodesFileNames()
    {
        WriteFile(_right, "views/public.a&b.sql", "SELECT 1;");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        var writer = new SchemaDiffReportWriter();
        var html = writer.BuildReport(result, DiffFormat.Html);

        Assert.Contains("a&amp;b", html);
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
