using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class SqlStatementSplitterBenchmark
{
    private readonly SqlStatementSplitter _splitter = new();
    private string _dump = "";

    [GlobalSetup]
    public void Setup()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 5_000; i++)
        {
            sb.AppendLine($"""
-- comment for statement {i}
CREATE TABLE public."t{i}" (
    "id" integer NOT NULL,
    "name" character varying(200) COLLATE "en_US" DEFAULT 'unknown',
    "created" timestamp with time zone DEFAULT now()
);
""");
            if (i % 100 == 0)
            {
                sb.AppendLine($"""
CREATE OR REPLACE FUNCTION public."fn{i}"() RETURNS void AS $$
BEGIN
    RAISE NOTICE 'hello {i}';
END;
$$ LANGUAGE plpgsql;
""");
            }
        }
        _dump = sb.ToString();
    }

    [Benchmark]
    public IReadOnlyList<string> Split() => _splitter.Split(_dump);
}
