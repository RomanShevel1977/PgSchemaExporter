using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class SqlStatementSplitterDollarQuotedBenchmark
{
    private readonly SqlStatementSplitter _splitter = new();
    private string _dump = "";

    [GlobalSetup]
    public void Setup()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 2_000; i++)
        {
            // Interleave plain CREATE TABLEs with dollar-quoted functions
            // and escaped string literals to stress the parser.
            sb.AppendLine($"""
-- statement {i}
CREATE TABLE public."t{i}" (
    "id" integer NOT NULL
);
""");

            if (i % 5 == 0)
            {
                sb.AppendLine($"""
CREATE OR REPLACE FUNCTION public."fn{i}"() RETURNS void AS $func${i}
BEGIN
    RAISE NOTICE 'hello {i}''s world';
END;
$func$ LANGUAGE plpgsql;
""");
            }

            if (i % 7 == 0)
            {
                sb.AppendLine($"""
INSERT INTO public."t{i}" VALUES (E'escaped\\n{i}', U&'\\0041{i}');
""");
            }
        }
        _dump = sb.ToString();
    }

    [Benchmark]
    public IReadOnlyList<string> Split() => _splitter.Split(_dump);
}
