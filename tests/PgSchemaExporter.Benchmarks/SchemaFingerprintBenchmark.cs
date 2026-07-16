using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Integrity;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class SchemaFingerprintBenchmark
{
    private string _directory = "";

    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pgschema-fp-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_directory);

        for (var i = 0; i < 1_000; i++)
        {
            var relative = Path.Combine("tables", $"public.t{i}.sql").Replace('\\', '/');
            var full = Path.Combine(_directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, $"""
CREATE TABLE public."t{i}" (
    "id" integer NOT NULL,
    "name" character varying(200)
);

""");
        }
    }

    [Benchmark]
    public SchemaFingerprintResult Compute() => SchemaFingerprint.Compute(_directory);
}
