using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaDiffReportWriterIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public SchemaDiffReportWriterIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-report-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static SchemaDiffResult CreateResult()
    {
        return new SchemaDiffResult
        {
            Added = ["tables/added.sql"],
            Removed = ["tables/removed.sql"],
            Changed = ["tables/changed.sql"],
            Unchanged = ["tables/unchanged.sql"],
            Statistics =
            [
                new DiffTypeStat { ObjectType = "table", Added = 1, Removed = 1, Changed = 1 }
            ],
            FileDiffs =
            [
                new FileDiff
                {
                    Path = "tables/changed.sql",
                    Lines =
                    [
                        new DiffLine { Kind = DiffLineKind.Removed, Text = "CREATE TABLE t (id int);" },
                        new DiffLine { Kind = DiffLineKind.Added, Text = "CREATE TABLE t (id int, name text);" }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void BuildReport_TextFormat_ContainsSummary()
    {
        var result = CreateResult();
        var writer = new SchemaDiffReportWriter();

        var report = writer.BuildReport(result, DiffFormat.Text);

        Assert.Contains("# Schema diff report", report);
        Assert.Contains("Added:     1", report);
        Assert.Contains("Removed:   1", report);
        Assert.Contains("Changed:   1", report);
        Assert.Contains("tables/added.sql", report);
        Assert.Contains("tables/removed.sql", report);
        Assert.Contains("tables/changed.sql", report);
    }

    [Fact]
    public async Task WriteAsync_TextFormat_WritesFile()
    {
        var result = CreateResult();
        var writer = new SchemaDiffReportWriter();
        var outputPath = Path.Combine(_tempRoot, "report.txt");

        await writer.WriteAsync(outputPath, result, DiffFormat.Text);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("# Schema diff report", content);
    }

    [Fact]
    public async Task WriteAsync_JsonFormat_WritesFile()
    {
        var result = CreateResult();
        var writer = new SchemaDiffReportWriter();
        var outputPath = Path.Combine(_tempRoot, "report.json");

        await writer.WriteAsync(outputPath, result, DiffFormat.Json);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("\"hasDifferences\": true", content);
        Assert.Contains("\"tables/added.sql\"", content);
    }

    [Fact]
    public async Task WriteAsync_HtmlFormat_WritesFile()
    {
        var result = CreateResult();
        var writer = new SchemaDiffReportWriter();
        var outputPath = Path.Combine(_tempRoot, "report.html");

        await writer.WriteAsync(outputPath, result, DiffFormat.Html);

        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("Schema diff report", content);
    }

    [Fact]
    public async Task WriteAsync_CreatesNestedDirectory()
    {
        var result = CreateResult();
        var writer = new SchemaDiffReportWriter();
        var outputPath = Path.Combine(_tempRoot, "nested", "dir", "report.txt");

        await writer.WriteAsync(outputPath, result, DiffFormat.Text);

        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void BuildReport_JsonFormat_ContainsFileDiffs()
    {
        var result = CreateResult();
        var writer = new SchemaDiffReportWriter();

        var json = writer.BuildReport(result, DiffFormat.Json);

        Assert.Contains("\"fileDiffs\"", json);
        Assert.Contains("\"kind\": \"removed\"", json);
        Assert.Contains("\"kind\": \"added\"", json);
    }
}
