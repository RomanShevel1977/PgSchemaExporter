using PgSchemaExporter.Cli;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class CliParserTests
{
    [Fact]
    public void ParseDiffOptions_EmptySchemas_Throws()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--schemas", "" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDiffOptions(args));
        Assert.Contains("--schemas cannot be empty", ex.Message);
    }

    [Fact]
    public void ParseDiffOptions_EmptyExcludeSchemas_Throws()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--exclude-schemas", "" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDiffOptions(args));
        Assert.Contains("--exclude-schemas cannot be empty", ex.Message);
    }

    [Fact]
    public void ParseDiffOptions_MissingLeft_Throws()
    {
        var args = new[] { "--right", "/right" };

        var options = CliParser.ParseDiffOptions(args);
        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Fact]
    public void ParseDiffOptions_MissingRight_Throws()
    {
        var args = new[] { "--left", "/left" };

        var options = CliParser.ParseDiffOptions(args);
        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Fact]
    public void ParseDiffOptions_InvalidFormat_Throws()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--format", "invalid" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDiffOptions(args));
        Assert.Contains("Unknown format", ex.Message);
    }

    [Fact]
    public void ParseDiffOptions_UnknownArgument_Throws()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--unknown" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDiffOptions(args));
        Assert.Contains("Unknown argument", ex.Message);
    }

    [Fact]
    public void ParseDiffOptions_MissingValueForArgument_Throws()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--schemas" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDiffOptions(args));
        Assert.Contains("Missing value for --schemas", ex.Message);
    }

    [Fact]
    public void ParseDiffOptions_WithLeftAndRight_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal("/left", options.LeftDirectory);
        Assert.Equal("/right", options.RightDirectory);
    }

    [Fact]
    public void ParseDiffOptions_WithLeftDbAndRightDb_ParsesCorrectly()
    {
        var args = new[] { "--left-db", "conn1", "--right-db", "conn2" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal("conn1", options.LeftConnectionString);
        Assert.Equal("conn2", options.RightConnectionString);
    }

    [Fact]
    public void ParseDiffOptions_WithSchemas_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--schemas", "public,app,test" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(["public", "app", "test"], options.Schemas);
    }

    [Fact]
    public void ParseDiffOptions_WithExcludeSchemas_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--exclude-schemas", "pg_catalog,information_schema" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(["pg_catalog", "information_schema"], options.ExcludeSchemas);
    }

    [Fact]
    public void ParseDiffOptions_WithParallel_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--parallel" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.True(options.Parallel);
    }

    [Fact]
    public void ParseDiffOptions_WithIgnoreComments_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--ignore-comments" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.True(options.IgnoreComments);
    }

    [Fact]
    public void ParseDiffOptions_WithIgnoreWhitespace_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--ignore-whitespace" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.True(options.IgnoreWhitespace);
    }

    [Fact]
    public void ParseDiffOptions_WithContext_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--context" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.True(options.ShowContext);
    }

    [Fact]
    public void ParseDiffOptions_WithFormatText_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--format", "text" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Text, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithFormatJson_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--format", "json" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Json, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithFormatHtml_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--format", "html" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Html, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithOutputFile_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--output", "report.txt" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal("report.txt", options.OutputFile);
    }

    [Fact]
    public void ParseDiffOptions_WithHtmlOutputFile_InfersFormat()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--output", "report.html" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Html, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithJsonOutputFile_InfersFormat()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--output", "report.json" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Json, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithHtmOutputFile_InfersFormat()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--output", "report.htm" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Html, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithExplicitFormatAndOutputFile_UsesExplicitFormat()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--format", "json", "--output", "report.html" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(DiffFormat.Json, options.Format);
    }

    [Fact]
    public void ParseDiffOptions_WithShortAliases_ParsesCorrectly()
    {
        var args = new[] { "-l", "/left", "-r", "/right", "-o", "report.txt" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal("/left", options.LeftDirectory);
        Assert.Equal("/right", options.RightDirectory);
        Assert.Equal("report.txt", options.OutputFile);
    }

    [Fact]
    public void ParseDiffOptions_WithSchemasWithSpaces_ParsesCorrectly()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--schemas", "public, app , test" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(["public", "app", "test"], options.Schemas);
    }

    [Fact]
    public void ParseDiffOptions_WithSchemasWithEmptyEntries_IgnoresEmpty()
    {
        var args = new[] { "--left", "/left", "--right", "/right", "--schemas", "public,,test" };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal(["public", "test"], options.Schemas);
    }

    [Fact]
    public void ParseDiffOptions_WithAllNewFlags_ParsesCorrectly()
    {
        var args = new[]
        {
            "--left", "/left",
            "--right", "/right",
            "--schemas", "public,app",
            "--exclude-schemas", "pg_catalog",
            "--parallel",
            "--ignore-comments",
            "--ignore-whitespace",
            "--context",
            "--output", "report.html"
        };

        var options = CliParser.ParseDiffOptions(args);

        Assert.Equal("/left", options.LeftDirectory);
        Assert.Equal("/right", options.RightDirectory);
        Assert.Equal(["public", "app"], options.Schemas);
        Assert.Equal(["pg_catalog"], options.ExcludeSchemas);
        Assert.True(options.Parallel);
        Assert.True(options.IgnoreComments);
        Assert.True(options.IgnoreWhitespace);
        Assert.True(options.ShowContext);
        Assert.Equal("report.html", options.OutputFile);
        Assert.Equal(DiffFormat.Html, options.Format);
    }
}
