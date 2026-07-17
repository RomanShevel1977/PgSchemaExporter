# Benchmark comparison

Comparing current branch results in `BenchmarkDotNet.Artifacts\results` against baseline in `BenchmarkDotNet.Artifacts\results-baseline`.

| Benchmark | Method | Baseline Mean | Current Mean | Mean Ratio | Baseline Allocated | Current Allocated | Allocated Ratio |
|---|---|---|---|---|---|---|---|
| PgSchemaExporter.Benchmarks.DeploymentPlanBuilderBenchmark-report.csv | Build | 76.87 ms | 5.762 ms | 7.5% | 27.14 MB | 4.89 MB | 18.0% |
| PgSchemaExporter.Benchmarks.LineDifferBenchmark-report.csv | Diff | 5.334 ms | 5.371 ms | 100.7% | 53.19 KB | 53.19 KB | 100.0% |
| PgSchemaExporter.Benchmarks.MigrationGeneratorBenchmark-report.csv | Generate | 43.81 ms | 44.70 ms | 102.0% | 3.13 MB | 3.15 MB | 100.6% |
| PgSchemaExporter.Benchmarks.MigrationGeneratorCacheHitBenchmark-report.csv | Generate | 36.99 ms | 36.08 ms | 97.5% | 1.7 MB | 1.7 MB | 100.0% |
| PgSchemaExporter.Benchmarks.PostgresMetadataProviderBenchmark-report.csv | LoadMetadata | 73.05 ms | 44.98 ms | 61.6% | 128.09 KB | 160.78 KB | 125.5% |
| PgSchemaExporter.Benchmarks.SchemaFingerprintBenchmark-report.csv | Compute | 181.2 ms | 23.08 ms | 12.7% | 8.87 MB | 8.88 MB | 100.1% |
| PgSchemaExporter.Benchmarks.SqlStatementSplitterBenchmark-report.csv | Split | 295.7 μs | 243.5 μs | 82.3% | 0 B | 0 B |  |
| PgSchemaExporter.Benchmarks.SqlStatementSplitterDollarQuotedBenchmark-report.csv | Split | 47.00 μs | 45.44 μs | 96.7% | 0 B | 0 B |  |
