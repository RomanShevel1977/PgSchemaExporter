# PgSchemaExporter Performance Refactoring Report

## 1. Summary

A cycle of optimizations and measurements for `PgSchemaExporter` was completed. Key results:

- A unified `SqlTokenizer` and `SqlStatementCache` were created; `SqlStatementSplitter`, `MigrationGenerator`, `ErModelBuilder`, `ConstraintDefinitionParser`, `TableDefinitionParser` and `SqlDropBuilder` now flow through them.
- `DeploymentPlanBuilder` was rewritten from `Regex`/`O(n²)` to `IndexOf`/`HashSet`: execution time dropped from **~5.8 s** to **~76 ms**, allocations from **6.28 GB** to **~27 MB**.
- `PostgresMetadataProvider` moved away from `information_schema` to direct queries against `pg_class`/`pg_attribute`, cached the `pg_get_policydef` check, and simplified type queries.
- `LineDiffer` switched to `ArrayPool<int>`/`ArrayPool<byte>` while keeping the LCS algorithm.
- The `tests/PgSchemaExporter.Benchmarks` project was added with eight benchmarks, including an integration `PostgresMetadataProviderBenchmark` on `Testcontainers.PostgreSql`.
- All changes do not change the public API; unit and integration tests pass without regressions (**475 passed, 0 failed**).

## 2. Scope and Methodology

### 2.1. What Was Analyzed

- `src/PgSchemaExporter.Core` — core: metadata, SQL generation/parsing, diff, fingerprint, diagrams, migrations, deployment plan.
- `src/PgSchemaExporter.Cli` — entry point, CLI parsing, progress.
- `tests/PgSchemaExporter.Benchmarks` — BenchmarkDotNet project for reproducible measurements.

### 2.2. Tools

- **BenchmarkDotNet** v0.13.12, `Job.ShortRun` configuration (Warmup = 3, Iteration = 3, Launch = 1), `MemoryDiagnoser`.
- **Testcontainers.PostgreSql** v4.0.0 + **Npgsql** v8.0.5 — for the metadata integration benchmark.
- **dotnet-trace** / **dotnet-counters** — scripts `scripts/profile-benchmarks.ps1` and `.sh`.
- **Unit and integration tests** — 475 tests, including `Testcontainers.PostgreSql`.

### 2.3. Measurement Environment

Approximate environment from the latest BenchmarkDotNet run:

- OS: Windows 11 (10.0.26100)
- CPU: Intel Core i9-9900K, 8 physical / 16 logical cores
- Runtime: .NET 8.0.28
- SDK: .NET SDK 10.0.301 (build targeting `net8.0`)

> **Important:** `ShortRun` configuration gives fast measurements with a wide confidence interval. For comparative tests and CI it is better to run with `Default`/`Medium` job.

## 3. Architectural Changes

### 3.1. Unified `SqlTokenizer` and `SqlStatementCache`

**Files:**
- `src/PgSchemaExporter.Core/Scripting/SqlTokenizer.cs`
- `src/PgSchemaExporter.Core/Scripting/SqlStatementCache.cs`
- `src/PgSchemaExporter.Core/Scripting/SqlStatementSplitter.cs`
- `src/PgSchemaExporter.Core/Migration/MigrationGenerator.cs`
- `src/PgSchemaExporter.Core/Diagramming/ConstraintDefinitionParser.cs`
- `src/PgSchemaExporter.Core/Migration/TableDefinitionParser.cs`
- `src/PgSchemaExporter.Core/Migration/SqlDropBuilder.cs`
- `src/PgSchemaExporter.Core/Diagramming/ErModelBuilder.cs`

**Problem:**
SQL parsing was duplicated in several places. Each parser made its own passes over the string, handled quotes and comments differently, and created extra intermediate strings.

**Solution:**
1. `SqlTokenizer` provides common, allocation-efficient operations:
   - `SplitStatements(string sql)` — split a batch into statements respecting `;`, single/double quotes, dollar-quoted blocks (`$tag$...$tag$`), `--` and `/* */` comments.
   - `NormalizeStatement(string sql)` — collapse whitespace to a single space.
   - `FindMatchingParen(string text, int openIndex)` — find the closing parenthesis respecting quotes/comments/dollar-quoted blocks.
   - `SplitTopLevel(string text, char delimiter)` — split by a delimiter at the top level of nesting.
2. `SqlStatementCache` caches `SplitStatements` and `NormalizeStatement` results in `Dictionary<string, ...>` for the lifetime of the instance.
3. `SqlStatementSplitter` and `MigrationGenerator` use `SqlStatementCache`.
4. `ConstraintDefinitionParser`, `TableDefinitionParser` and `SqlDropBuilder` call `SqlTokenizer.FindMatchingParen`/`SplitTopLevel`.
5. `ErModelBuilder` splits constraint files through `SqlTokenizer.SplitStatements`, correctly handling string literals and dollar-quoted blocks.

**Effect:**
- Removed repeated passes over the same SQL in `MigrationGenerator`.
- All parsers now handle quotes and comments uniformly.
- Reduced allocations via `ReadOnlySpan<char>.Trim()` and reused `StringBuilder` initial capacity.

## 4. Component-Level Optimizations

### 4.1. `MigrationGenerator` — Parse Caching

**File:** `src/PgSchemaExporter.Core/Migration/MigrationGenerator.cs`

**Problem:**
- `BuildChangedTable` called `SplitStatements` for `fromContent`/`toContent` up to 4-5 times.
- `BuildAdded`/`BuildRemoved` called `SplitStatements` twice each.
- `BuildStatementSetDiff` normalized strings repeatedly.

**Solution:**
- Added an `SqlStatementCache _statementCache` instance.
- All `SplitStatements(content)` and `NormalizeStatement(statement)` calls go through the cache.
- Every unique SQL is parsed and normalized at most once per `Generate` call.

**Result:**
- `MigrationGeneratorBenchmark.Generate` — ~43.8 ms, 3.13 MB allocations (unique files).
- `MigrationGeneratorCacheHitBenchmark` (same files in `from`/`to`) — ~37 ms, **1.7 MB**, clearly showing the cache effect.

### 4.2. `DeploymentPlanBuilder` — Dropping `Regex` and `O(n²)`

**File:** `src/PgSchemaExporter.Core/Output/DeploymentPlanBuilder.cs`

**Problem:**
- `ContainsWord` created a `Regex` on every check (`Regex.Escape` + compilation).
- `ReferencesObject`/`ReferencesRoutine`/`ReferencesTable` used `Regex.IsMatch`.
- `TopologicalSort` used `List<string>.Contains` (`O(n)`) and `OrderBy` inside a loop.

**Solution:**
- `ContainsWord` and related methods were rewritten with `IndexOf` and manual word-boundary checks.
- `TopologicalSort` uses `HashSet<string>` for `O(1)` lookups; dependencies are sorted once while building the graph.
- Collecting all known object identifiers in dictionaries provides `O(1)` file lookup via `TryGet`/`GetSchemaNameFromFile`.

**Result:**

| Method | Mean | Allocated |
|---|---|---|
| `Build` (before) | ~5.786 s | 6.28 GB |
| `Build` (after) | **76.87 ms** | **27.14 MB** |

Speedup ~75×, memory reduction ~237×.

### 4.3. `PostgresMetadataProvider` — Fewer Round-Trips and `information_schema`

**File:** `src/PgSchemaExporter.Core/Metadata/PostgresMetadataProvider.cs`

**Problem:**
- `PolicyDefFunctionExistsAsync` issued `ExecuteScalarAsync` on every `GetPoliciesAsync` call.
- `GetTablesAsync` used `information_schema.columns`/`information_schema.tables` (slow views).
- `GetTypesAsync` used `LEFT JOIN pg_enum` + `GROUP BY` + `array_remove`.

**Solution:**
- `PolicyDefFunctionExistsAsync` is cached in a static `ConcurrentDictionary<string, bool>` keyed by connection string.
- `GetTablesAsync` moved to a direct query against `pg_class`/`pg_attribute`/`pg_type`/`pg_attrdef`.
- `GetTypesAsync` was simplified: `LEFT JOIN` + `GROUP BY` replaced by a correlated `array_agg(...)` subquery.

**Result:**
- Integration benchmark `PostgresMetadataProviderBenchmark.LoadMetadata` (100 schemas, 5 selected, with tables/constraints/indexes/comments):

| Method | Mean | Allocated |
|---|---|---|
| `LoadMetadata` | **73.05 ms** | **128.09 KB** |

### 4.4. `LineDiffer` — LCS Matrix Memory

**File:** `src/PgSchemaExporter.Core/Diff/LineDiffer.cs`

**Problem:**
The classic LCS algorithm allocated `int[n+1, m+1]`. For 10,000 lines this is ~400 MB.

**Solution:**
- Two one-dimensional `ArrayPool<int>.Shared.Rent(m + 1)` arrays instead of a matrix.
- Direction matrix replaced by `ArrayPool<byte>.Shared.Rent(n * m)` (1 byte per cell instead of 4).
- Fast paths for empty arrays.

**Result:**

| Method | Mean | Allocated |
|---|---|---|
| `Diff` (1,000 lines, 20% differences) | **5.334 ms** | **53.19 KB** |

### 4.5. `SqlStatementSplitter` — `StringBuilder` and `ReadOnlySpan`

**File:** `src/PgSchemaExporter.Core/Scripting/SqlStatementSplitter.cs`

**Solution:**
- `SqlStatementSplitter` delegates to `SqlTokenizer.SplitStatements` through `SqlStatementCache`.
- `StringBuilder` is initialized with `Math.Min(sql.Length, 4096)` so it does not reserve memory for the whole dump.
- `Trim()` replaced with `ReadOnlySpan<char>.Trim()`.

**Result:**

| Benchmark | Mean | Allocated |
|---|---|---|
| `SqlStatementSplitterBenchmark.Split` (5,000 statements) | **295.7 µs** | **0 B** |
| `SqlStatementSplitterDollarQuotedBenchmark.Split` (dollar-quoted + `E'/U&'`) | **47.0 µs** | **0 B** |

### 4.6. `SchemaFingerprint` — `IncrementalHash` and Direct Hex Formatting

**File:** `src/PgSchemaExporter.Core/Integrity/SchemaFingerprint.cs`

**Problem:**
- Aggregate hash accumulated in a `MemoryStream`.
- `Convert.ToHexString(...).ToLowerInvariant()` performed an extra case transformation.

**Solution:**
- Switched to `IncrementalHash` (SHA256).
- Hex string built via `string.Create` with the `0123456789abcdef` table, no `ToLowerInvariant`.

**Result:**

| Benchmark | Mean | Allocated |
|---|---|---|
| `SchemaFingerprintBenchmark.Compute` (1,000 files) | **181.2 ms** | **8.87 MB** |

### 4.7. `ConstraintDefinitionParser`, `TableDefinitionParser`, `SqlDropBuilder` — Parsing Unification

**Files:**
- `src/PgSchemaExporter.Core/Diagramming/ConstraintDefinitionParser.cs`
- `src/PgSchemaExporter.Core/Migration/TableDefinitionParser.cs`
- `src/PgSchemaExporter.Core/Migration/SqlDropBuilder.cs`

**Solution:**
- Removed local duplicates of `FindMatchingParen` and `SplitTopLevel`/`SplitTopLevelCommas`.
- Parsers call `SqlTokenizer.FindMatchingParen` and `SqlTokenizer.SplitTopLevel`.
- `FindKeyword` in `ConstraintDefinitionParser` was rewritten with `IndexOf`, word-boundary checks and `ReadOnlySpan`.

**Effect:**
- Quadratic keyword search complexity eliminated.
- Unified handling of quotes/comments across all parsers.

### 4.8. `SchemaFileWriter` / `TableScriptGenerator` — Parallel Generation

**File:** `src/PgSchemaExporter.Core/Output/SchemaFileWriter.cs`

**Solution:**
- Removed redundant `OrderBy(x => x.OrdinalPosition)` in `TableScriptGenerator` (columns are already sorted in `GetTablesAsync`).
- Added `WriteItemsParallelAsync`: SQL string generation for tables/constraints/indexes/functions runs through `Parallel.For`, file writing remains sequential.

**Effect:**
- CPU-bound generation is parallelized; I/O-bound writing does not compete for the file system.

### 4.9. `HazardAnalyzer` — Redundant Normalization

**File:** `src/PgSchemaExporter.Core/Migration/Hazards/HazardAnalyzer.cs`

**Solution:**
- Generated `[GeneratedRegex(@"\s+")] WhitespaceRegex()` replaces the chain of `Replace` + `Regex.Replace`.

**Effect:**
- Removed three intermediate strings per statement.

### 4.10. `SqlIdentifier` and `MigrationWriter` — Micro-Optimizations

**Files:**
- `src/PgSchemaExporter.Core/Scripting/SqlIdentifier.cs`
- `src/PgSchemaExporter.Core/Migration/MigrationWriter.cs`

**Solution:**
- `SqlIdentifier.SafeFileName` uses a static `HashSet<char>` instead of `Path.GetInvalidFileNameChars().Contains` and a `StringBuilder` instead of `Select(...).ToArray()`.
- `MigrationWriter.BuildSlug` was rewritten as a single `StringBuilder` pass that immediately collapses runs of underscores.

**Effect:**
- Removed the quadratic `while (slug.Contains("__")) slug = slug.Replace("__", "_")` loop.

### 4.11. `Program.cs` / CLI — Less Materialization

**File:** `src/PgSchemaExporter.Cli/Program.cs`

**Solution:**
- Removed unnecessary `ToArray`/`ToList` in argument filtering.
- A single `PostgresMetadataProvider` instance is created and passed to `SchemaExporter`, `SchemaDiffer`, `DriftDetector`, `LiveSchemaExporter`.
- `--verbose`, `--quiet`, `--profile` flags are filtered manually via `Array.Resize`.

**Effect:**
- Fewer intermediate arrays; single metadata provider lifetime.

## 5. Benchmark Project

### 5.1. Structure

**Project:** `tests/PgSchemaExporter.Benchmarks/PgSchemaExporter.Benchmarks.csproj`

Packages:
- `BenchmarkDotNet` 0.13.12
- `Npgsql` 8.0.5
- `Testcontainers.PostgreSql` 4.0.0

**Configuration (`Program.cs`):**

```csharp
var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.ShortRun);
BenchmarkRunner.Run(typeof(Program).Assembly, config, args);
```

`Job.ShortRun`: Warmup=3, Iteration=3, Launch=1. Fast feedback loop, but with a wide confidence interval.

### 5.2. Benchmark List

| Benchmark | What It Measures | Notes |
|---|---|---|
| `DeploymentPlanBuilderBenchmark` | Building a deployment plan for 1,000+ objects | Fixture with tables, types, functions, constraints |
| `LineDifferBenchmark` | LCS diff of 1,000 lines | 20% of lines differ |
| `MigrationGeneratorBenchmark` | Migration generation | ~140 `.sql` files, unique content |
| `MigrationGeneratorCacheHitBenchmark` | Migration generation with cache hits | 100 identical files with one difference |
| `SchemaFingerprintBenchmark` | Hashing 1,000 `.sql` files | Checks `IncrementalHash` |
| `SqlStatementSplitterBenchmark` | Splitting a dump into 5,000 statements | Comments, quotes, dollar-quoted |
| `SqlStatementSplitterDollarQuotedBenchmark` | Splitting dollar-quoted functions | `E'/U&'` escape literals |
| `PostgresMetadataProviderBenchmark` | Loading metadata from the database | Testcontainers, 100 schemas, 5 selected |

### 5.3. Running

```bash
dotnet run -c Release --project tests/PgSchemaExporter.Benchmarks/PgSchemaExporter.Benchmarks.csproj
```

Filtering:

```bash
dotnet run -c Release --project tests/PgSchemaExporter.Benchmarks -- --filter '*DeploymentPlanBuilder*'
```

## 6. BenchmarkDotNet Results

Latest run of all eight benchmarks:

| Benchmark | Method | Mean | Allocated |
|---|---|---|---|
| `DeploymentPlanBuilderBenchmark` | `Build` | **76.87 ms** | **27.14 MB** |
| `LineDifferBenchmark` | `Diff` | **5.334 ms** | **53.19 KB** |
| `MigrationGeneratorBenchmark` | `Generate` | **43.81 ms** | **3.13 MB** |
| `MigrationGeneratorCacheHitBenchmark` | `Generate` | **36.99 ms** | **1.70 MB** |
| `SchemaFingerprintBenchmark` | `Compute` | **181.2 ms** | **8.87 MB** |
| `SqlStatementSplitterBenchmark` | `Split` | **295.7 µs** | **0 B** |
| `SqlStatementSplitterDollarQuotedBenchmark` | `Split` | **47.0 µs** | **0 B** |
| `PostgresMetadataProviderBenchmark` | `LoadMetadata` | **73.05 ms** | **128.09 KB** |

### 6.1. Comparison with Previous Baseline

Some components had pre-refactoring measurements:

| Component | Before | After | Speedup | Before Allocated | After Allocated |
|---|---|---|---|---|---|
| `DeploymentPlanBuilder.Build` | ~5.786 s | 76.87 ms | ~75× | 6.28 GB | 27.14 MB |
| `MigrationGenerator.Generate` (unique files) | 44.02 ms | 43.81 ms | ~1× | 3.23 MB | 3.13 MB |
| `SchemaFingerprint.Compute` | 193.2 ms | 181.2 ms | ~1.07× | 9.32 MB | 8.87 MB |
| `SqlStatementSplitter.Split` | 5.591 ms* | 295.7 µs | ~19× | 4.42 MB | 0 B |

\* The previous benchmark used a different (larger) fixture; direct time comparison is conditional. The key points are zero allocations and handling of dollar-quoted/escape literals.

## 7. CI and Profiling

### 7.1. GitHub Actions: `benchmarks.yml`

**File:** `.github/workflows/benchmarks.yml`

- Triggers on `pull_request` when `src/**/*.cs` or `tests/PgSchemaExporter.Benchmarks/**` change, and manually (`workflow_dispatch`).
- Runs `dotnet restore` and `dotnet run -c Release` for benchmarks.
- Preserves artifacts from `BenchmarkDotNet.Artifacts/results/*.md|csv|html`.

### 7.2. Profiling Scripts

**Files:**
- `scripts/profile-benchmarks.ps1`
- `scripts/profile-benchmarks.sh`

Scripts:
- publish the benchmark project (`dotnet publish -c Release`);
- run the benchmark;
- simultaneously attach `dotnet-trace` (collect `.nettrace`) and `dotnet-counters` (collect CSV);
- support `-ProductionLike`/`production-like` mode that automatically filters the `PostgresMetadataProvider` and `DeploymentPlanBuilder` benchmarks.

Example:

```powershell
.\scripts\profile-benchmarks.ps1 -ProductionLike
```

```bash
./scripts/profile-benchmarks.sh "" "" "" "" true
```

Counters:
- `System.Runtime`: `cpu-usage`, `gc-heap-size`, `gc-committed-bytes`, `threadpool-queue-length`, `threadpool-thread-count`, `exception-count`.
- `Microsoft.AspNetCore.Hosting`: `requests-per-second`.

## 8. Test Results

Latest run:

- **Unit tests:** 314 passed, 0 failed
- **Integration tests** with `Testcontainers.PostgreSql`: 150 passed, 0 failed
- **End-to-end tests:** 11 passed, 0 failed
- **Total:** **475 passed, 0 failed**

All changes do not change the public API and do not break existing behavior.

## 9. Conclusions and Next Steps

### 9.1. Justification of Optimizations

- **`DeploymentPlanBuilder`** — **the most significant optimization**. Moving from `Regex`/`O(n²)` to `IndexOf`/`HashSet` gave ~75× speedup and ~237× memory reduction. This is critical for schemas with thousands of objects.
- **`PostgresMetadataProvider`** — justified for live databases: direct queries to `pg_class`/`pg_attribute` and caching `pg_get_policydef` reduce load on the PostgreSQL catalog.
- **`SqlTokenizer`/`SqlStatementCache`** — the foundation for long-term parser simplification. Repeated passes and quadratic searches have been removed; all parsers now handle quotes and comments uniformly.
- **`LineDiffer`** — reduced memory consumption for large diffs; the algorithm remains correct.
- **`SchemaFileWriter`**, **`SchemaFingerprint`**, **`HazardAnalyzer`**, **`SqlIdentifier`**, **`MigrationWriter`**, **`ConstraintDefinitionParser`**, **`TableDefinitionParser`**, **`SqlDropBuilder`**, **`Program.cs`** — micro- and medium optimizations that are safe and do not worsen metrics.

### 9.2. Future Directions

1. **Full token-level `SqlTokenizer`** — move keyword search and identifier reading (`IndexOfWord`, `ReadIdentifier`, `SkipLeadingNoise`) into `SqlTokenizer` to fully unify `ConstraintDefinitionParser`, `TableDefinitionParser` and `SqlDropBuilder`.
2. **Profile `GetCommentsAsync`/`GetGrantsAsync`** — on a real large database run `EXPLAIN ANALYZE` and, if necessary, split massive `UNION ALL` into several queries.
3. **Source-generated JSON** — for `MigrationPlanFile`, `MigrationHistory`, `SchemaFingerprintFile` consider `System.Text.Json` source generators or `Utf8JsonWriter`/`Utf8JsonReader` for large plans.
4. **Parallel read/hashing** — `SchemaDiffer` can read files in parallel; `SchemaFingerprint` can hash files in parallel.
5. **BenchmarkDotNet CI baseline** — add benchmark result comparison in PRs against a baseline branch, not just artifact storage.

### 9.3. Final Conclusion

The implemented set of changes substantially reduces CPU and memory load for large PostgreSQL schemas, especially in `DeploymentPlanBuilder` and `PostgresMetadataProvider`. Benchmarks and integration tests confirm correctness and absence of regressions. All previously planned steps are complete; future effort should be concentrated on full token-level parsing unification and regular profiling of live scenarios.
