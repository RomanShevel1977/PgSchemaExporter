using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Diff;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class LineDifferBenchmark
{
    private string[] _left = [];
    private string[] _right = [];

    [GlobalSetup]
    public void Setup()
    {
        const int count = 1_000;

        _left = new string[count];
        _right = new string[count];

        for (var i = 0; i < count; i++)
        {
            _left[i] = $"line {i}";
            // 20% of the lines differ
            _right[i] = i % 5 == 0 ? $"modified line {i}" : _left[i];
        }
    }

    [Benchmark]
    public IReadOnlyList<DiffLine> Diff() => LineDiffer.Diff(_left, _right);
}
