using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Migration;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class MigrationGeneratorCacheHitBenchmark
{
    private string _fromDir = "";
    private string _toDir = "";
    private readonly MigrationGenerator _generator = new();

    [GlobalSetup]
    public void Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pgschema-mig-cache-{Guid.NewGuid():n}");
        _fromDir = Path.Combine(root, "from");
        _toDir = Path.Combine(root, "to");

        Directory.CreateDirectory(_fromDir);
        Directory.CreateDirectory(_toDir);

        // Many identical files to stress the SqlStatementCache.
        const string identicalContent = """
CREATE TABLE IF NOT EXISTS public."shared" (
    "id" integer NOT NULL,
    "name" character varying(200)
);
""";

        for (var i = 0; i < 100; i++)
        {
            Write(_fromDir, Path.Combine("tables", $"shared_{i}.sql"), identicalContent);
            Write(_toDir, Path.Combine("tables", $"shared_{i}.sql"), i == 50 ? identicalContent.Replace("integer", "bigint") : identicalContent);
        }
    }

    private static void Write(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Benchmark]
    public MigrationScript Generate() => _generator.Generate(new MigrationOptions
    {
        FromDirectory = _fromDir,
        ToDirectory = _toDir,
        Preview = true
    });
}
