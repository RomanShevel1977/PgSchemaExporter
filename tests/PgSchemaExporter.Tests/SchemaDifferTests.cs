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

    [Fact]
    public void Diff_IgnoreComments_TreatsCommentOnlyChangesAsUnchanged()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int); -- old comment");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int); -- new comment\n-- extra line");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreComments = true
        });

        Assert.False(result.HasDifferences);
        Assert.Single(result.Unchanged);
    }

    [Fact]
    public void Diff_IgnoreWhitespace_TreatsWhitespaceOnlyChangesAsUnchanged()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE   TABLE    users (id int);\n\n");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreWhitespace = true
        });

        Assert.False(result.HasDifferences);
        Assert.Single(result.Unchanged);
    }

    [Fact]
    public void Diff_ProducesStatisticsGroupedByObjectType()
    {
        WriteFile(_left, "tables/public.orders.sql", "CREATE TABLE orders (id int);");
        WriteFile(_right, "tables/public.orders.sql", "CREATE TABLE orders (id bigint);");
        WriteFile(_right, "views/public.v1.sql", "SELECT 1;");
        WriteFile(_left, "functions/public.f1.abc.sql", "SELECT 1;");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        var tables = Assert.Single(result.Statistics, s => s.ObjectType == "tables");
        Assert.Equal(1, tables.Changed);
        Assert.Equal(1, tables.Total);

        var views = Assert.Single(result.Statistics, s => s.ObjectType == "views");
        Assert.Equal(1, views.Added);

        var functions = Assert.Single(result.Statistics, s => s.ObjectType == "functions");
        Assert.Equal(1, functions.Removed);
    }

    [Fact]
    public void Diff_ShowContext_ProducesLineLevelDiff()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (\n  id int\n);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (\n  id bigint\n);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            ShowContext = true
        });

        var fileDiff = Assert.Single(result.FileDiffs);
        Assert.Equal("tables/public.users.sql", fileDiff.Path);
        Assert.Contains(fileDiff.Lines, l => l.Kind == DiffLineKind.Removed && l.Text.Contains("id int"));
        Assert.Contains(fileDiff.Lines, l => l.Kind == DiffLineKind.Added && l.Text.Contains("id bigint"));
        Assert.Contains(fileDiff.Lines, l => l.Kind == DiffLineKind.Context && l.Text.Contains("CREATE TABLE"));
    }

    [Fact]
    public void Diff_WithoutContext_DoesNotPopulateFileDiffs()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id bigint);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions { LeftDirectory = _left, RightDirectory = _right });

        Assert.Empty(result.FileDiffs);
        Assert.Single(result.Changed);
    }

    [Fact]
    public void DiffReportWriter_TextReport_IncludesStatisticsAndContext()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id bigint);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            ShowContext = true
        });

        var text = new SchemaDiffReportWriter().BuildReport(result, DiffFormat.Text);

        Assert.Contains("## Changes by type", text);
        Assert.Contains("tables: +0 -0 ~1", text);
        Assert.Contains("## Details", text);
        Assert.Contains("### tables/public.users.sql", text);
    }

    [Fact]
    public void LineDiffer_ProducesExpectedSequence()
    {
        var left = new[] { "a", "b", "c" };
        var right = new[] { "a", "x", "c" };

        var lines = LineDiffer.Diff(left, right);

        Assert.Collection(lines,
            l => { Assert.Equal(DiffLineKind.Context, l.Kind); Assert.Equal("a", l.Text); },
            l => { Assert.Equal(DiffLineKind.Removed, l.Kind); Assert.Equal("b", l.Text); },
            l => { Assert.Equal(DiffLineKind.Added, l.Kind); Assert.Equal("x", l.Text); },
            l => { Assert.Equal(DiffLineKind.Context, l.Kind); Assert.Equal("c", l.Text); });
    }

    [Fact]
    public void LineDiffer_EmptyArrays_ReturnsEmpty()
    {
        var left = Array.Empty<string>();
        var right = Array.Empty<string>();

        var lines = LineDiffer.Diff(left, right);

        Assert.Empty(lines);
    }

    [Fact]
    public void LineDiffer_OneEmptyArray_MarksAllAsAdded()
    {
        var left = Array.Empty<string>();
        var right = new[] { "a", "b", "c" };

        var lines = LineDiffer.Diff(left, right);

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(DiffLineKind.Added, l.Kind));
    }

    [Fact]
    public void LineDiffer_AllLinesDifferent_MarksAllAsRemovedAndAdded()
    {
        var left = new[] { "a", "b", "c" };
        var right = new[] { "x", "y", "z" };

        var lines = LineDiffer.Diff(left, right);

        Assert.Equal(6, lines.Count);
        Assert.Equal(DiffLineKind.Removed, lines[0].Kind);
        Assert.Equal(DiffLineKind.Removed, lines[1].Kind);
        Assert.Equal(DiffLineKind.Removed, lines[2].Kind);
        Assert.Equal(DiffLineKind.Added, lines[3].Kind);
        Assert.Equal(DiffLineKind.Added, lines[4].Kind);
        Assert.Equal(DiffLineKind.Added, lines[5].Kind);
    }

    [Fact]
    public void LineDiffer_AllLinesSame_MarksAllAsContext()
    {
        var left = new[] { "a", "b", "c" };
        var right = new[] { "a", "b", "c" };

        var lines = LineDiffer.Diff(left, right);

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(DiffLineKind.Context, l.Kind));
    }

    [Fact]
    public void LineDiffer_SingleCharLine_HandlesCorrectly()
    {
        var left = new[] { "-" };
        var right = new[] { "--" };

        var lines = LineDiffer.Diff(left, right);

        Assert.Equal(2, lines.Count);
        Assert.Equal(DiffLineKind.Removed, lines[0].Kind);
        Assert.Equal("-", lines[0].Text);
        Assert.Equal(DiffLineKind.Added, lines[1].Kind);
        Assert.Equal("--", lines[1].Text);
    }

    [Fact]
    public void Diff_IgnoreComments_EscapedQuotesInString_DoesNotStripComment()
    {
        WriteFile(_left, "tables/public.users.sql", "INSERT INTO users (name) VALUES ('O''Reilly'); -- comment");
        WriteFile(_right, "tables/public.users.sql", "INSERT INTO users (name) VALUES ('O''Reilly'); -- different comment");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreComments = true
        });

        Assert.False(result.HasDifferences);
        Assert.Single(result.Unchanged);
    }

    [Fact]
    public void Diff_IgnoreComments_CommentInsideString_DoesNotStrip()
    {
        WriteFile(_left, "tables/public.users.sql", "SELECT '-- not a comment' FROM users;");
        WriteFile(_right, "tables/public.users.sql", "SELECT '-- not a comment' FROM users;");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreComments = true
        });

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoreComments_CommentAtStart_StripsCorrectly()
    {
        WriteFile(_left, "tables/public.users.sql", "-- comment\nCREATE TABLE users (id int);");
        WriteFile(_right, "tables/public.users.sql", "-- different comment\nCREATE TABLE users (id int);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreComments = true
        });

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoreComments_CommentInMiddle_StripsCorrectly()
    {
        WriteFile(_left, "tables/public.users.sql", "SELECT * -- comment\nFROM users;");
        WriteFile(_right, "tables/public.users.sql", "SELECT * -- different comment\nFROM users;");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreComments = true
        });

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoreWhitespace_EmptyString_HandlesCorrectly()
    {
        WriteFile(_left, "tables/public.users.sql", "");
        WriteFile(_right, "tables/public.users.sql", "");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreWhitespace = true
        });

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoreWhitespace_AllWhitespace_HandlesCorrectly()
    {
        WriteFile(_left, "tables/public.users.sql", "   \t  \n  ");
        WriteFile(_right, "tables/public.users.sql", " \t \n ");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreWhitespace = true
        });

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoreWhitespace_TrailingSpaces_StripsCorrectly()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE TABLE users (id int);  ");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);   ");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreWhitespace = true
        });

        Assert.False(result.HasDifferences);
    }

    [Fact]
    public void Diff_IgnoreWhitespace_MultipleSpacesBetweenWords_CollapsesCorrectly()
    {
        WriteFile(_left, "tables/public.users.sql", "CREATE   TABLE    users (id int);");
        WriteFile(_right, "tables/public.users.sql", "CREATE TABLE users (id int);");

        var differ = new SchemaDiffer();
        var result = differ.Diff(new SchemaDiffOptions
        {
            LeftDirectory = _left,
            RightDirectory = _right,
            IgnoreWhitespace = true
        });

        Assert.False(result.HasDifferences);
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
