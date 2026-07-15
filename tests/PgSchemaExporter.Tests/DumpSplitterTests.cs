using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DumpSplitterTests : IDisposable
{
    private readonly string _root;
    private readonly string _inputFile;
    private readonly string _outputDir;
    private readonly DumpSplitter _splitter;

    public DumpSplitterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-split-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _inputFile = Path.Combine(_root, "dump.sql");
        _outputDir = Path.Combine(_root, "out");

        _splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());
    }

    [Fact]
    public async Task SplitAsync_ValidDump_WritesExpectedFiles()
    {
        var sql = """
CREATE SCHEMA public;
CREATE TABLE public.t (id integer NOT NULL);
CREATE INDEX idx ON public.t (id);
""";
        await File.WriteAllTextAsync(_inputFile, sql);

        await _splitter.SplitAsync(new SplitDumpOptions { InputFile = _inputFile, OutputDirectory = _outputDir, CleanOutputDirectory = true });

        Assert.True(File.Exists(Path.Combine(_outputDir, "deploy.sql")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "dependencies.json")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "schemas", "public.public.sql")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "tables", "public.t.sql")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "indexes", "public.t.indexes.sql")));
    }

    [Fact]
    public async Task SplitAsync_CleanOutput_DeletesPreviousFiles()
    {
        var oldFile = Path.Combine(_outputDir, "old.txt");
        Directory.CreateDirectory(_outputDir);
        await File.WriteAllTextAsync(oldFile, "old");

        await File.WriteAllTextAsync(_inputFile, "CREATE SCHEMA public;");
        await _splitter.SplitAsync(new SplitDumpOptions { InputFile = _inputFile, OutputDirectory = _outputDir, CleanOutputDirectory = true });

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public async Task SplitAsync_SkipDeployScript_DoesNotWriteDeploySql()
    {
        await File.WriteAllTextAsync(_inputFile, "CREATE SCHEMA public;");
        await _splitter.SplitAsync(new SplitDumpOptions { InputFile = _inputFile, OutputDirectory = _outputDir, GenerateDeployScript = false });

        Assert.False(File.Exists(Path.Combine(_outputDir, "deploy.sql")));
    }

    [Fact]
    public async Task SplitAsync_WritesSplitReport()
    {
        await File.WriteAllTextAsync(_inputFile, "CREATE SCHEMA public;");
        await _splitter.SplitAsync(new SplitDumpOptions { InputFile = _inputFile, OutputDirectory = _outputDir });

        var report = Path.Combine(_outputDir, "split-report.md");
        Assert.True(File.Exists(report));
        var content = await File.ReadAllTextAsync(report);
        Assert.Contains("Total statements: 1", content);
    }

    [Fact]
    public async Task SplitAsync_MissingInputFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _splitter.SplitAsync(new SplitDumpOptions { InputFile = Path.Combine(_root, "missing.sql"), OutputDirectory = _outputDir }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
