# Benchmark comparison

Comparing current branch results in `BenchmarkDotNet.Artifacts\results` against baseline in `BenchmarkDotNet.Artifacts\results-baseline`.

| Benchmark | Method | Baseline Mean | Current Mean | Mean Ratio | Baseline Allocated | Current Allocated | Allocated Ratio |
|---|---|---|---|---|---|---|---|
| PgSchemaExporter.Benchmarks.DeploymentPlanBuilderBenchmark-report.csv | Build | 76.87 ms | 5.896 ms | 7.7% | 27.14 MB | 4.26 MB | 15.7% |
| PgSchemaExporter.Benchmarks.LineDifferBenchmark-report.csv | Diff | 5.334 ms | 5.397 ms | 101.2% | 53.19 KB | 53.19 KB | 100.0% |
| PgSchemaExporter.Benchmarks.MigrationGeneratorBenchmark-report.csv | Generate | 43.81 ms | 46.18 ms | 105.4% | 3.13 MB | 4.3 MB | 137.4% |
| PgSchemaExporter.Benchmarks.MigrationGeneratorCacheHitBenchmark-report.csv | Generate | 36.99 ms | 37.04 ms | 100.1% | 1.7 MB | 1.72 MB | 101.2% |
| PgSchemaExporter.Benchmarks.PostgresMetadataProviderBenchmark-report.csv | LoadMetadata | 73.05 ms | 45.36 ms | 62.1% | 128.09 KB | 160.65 KB | 125.4% |
| PgSchemaExporter.Benchmarks.SchemaFingerprintBenchmark-report.csv | Compute | 181.2 ms | 23.15 ms | 12.8% | 8.87 MB | 8.87 MB | 100.0% |
| PgSchemaExporter.Benchmarks.SqlStatementSplitterBenchmark-report.csv | Split | 295.7 μs | 292.8 μs | 99.0% | 0 B | 0 B |  |
| PgSchemaExporter.Benchmarks.SqlStatementSplitterDollarQuotedBenchmark-report.csv | Split | 47.00 μs | 48.02 μs | 102.2% | 0 B | 0 B |  |
