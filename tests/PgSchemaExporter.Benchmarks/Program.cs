using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace PgSchemaExporter.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.ShortRun);
        BenchmarkRunner.Run(typeof(Program).Assembly, config, args);
    }
}
