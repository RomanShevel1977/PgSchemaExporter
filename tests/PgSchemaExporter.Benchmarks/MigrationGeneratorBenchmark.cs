using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Migration;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class MigrationGeneratorBenchmark
{
    private string _fromDir = "";
    private string _toDir = "";
    private readonly MigrationGenerator _generator = new();

    [GlobalSetup]
    public void Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pgschema-mig-{Guid.NewGuid():n}");
        _fromDir = Path.Combine(root, "from");
        _toDir = Path.Combine(root, "to");

        Directory.CreateDirectory(_fromDir);
        Directory.CreateDirectory(_toDir);

        for (var i = 0; i < 100; i++)
        {
            var tablePath = Path.Combine("tables", $"public.t{i}.sql");
            Write(_fromDir, tablePath, $"""
CREATE TABLE IF NOT EXISTS public."t{i}" (
    "id" integer NOT NULL,
    "name" character varying(200),
    "created" timestamp with time zone DEFAULT now()
);
""");
            var changedType = i % 3 == 0 ? "bigint" : "integer";
            Write(_toDir, tablePath, $"""
CREATE TABLE IF NOT EXISTS public."t{i}" (
    "id" {changedType} NOT NULL,
    "name" character varying(200),
    "created" timestamp with time zone DEFAULT now()
);
""");
        }

        // some added/removed files to exercise all branches
        for (var i = 100; i < 120; i++)
            Write(_toDir, Path.Combine("tables", $"public.t{i}.sql"), $"""
CREATE TABLE IF NOT EXISTS public."t{i}" (
    "id" integer NOT NULL
);
""");

        for (var i = 120; i < 140; i++)
            Write(_fromDir, Path.Combine("tables", $"public.t{i}.sql"), $"""
CREATE TABLE IF NOT EXISTS public."t{i}" (
    "id" integer NOT NULL
);
""");
    }

    private static void Write(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Benchmark]
    public MigrationScript Generate()
    {
        return _generator.Generate(new MigrationOptions
        {
            FromDirectory = _fromDir,
            ToDirectory = _toDir,
            Preview = true
        });
    }
}
