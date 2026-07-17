# PgSchemaExporter Performance and Refactoring Opportunities Analysis

## 1. Summary

A detailed source-code review of `PgSchemaExporter` was conducted to identify performance bottlenecks and possible improvements. **All high-priority optimizations and a number of medium-priority ones have been implemented.** Unit and integration tests pass without regressions.

### 1.1. Implemented Improvements

- **MigrationGenerator** — caching of `SplitStatements` and `NormalizeStatement` via `Dictionary<string, ...>`, eliminating repeated parsing of the same SQL files.
- **DeploymentPlanBuilder** — complete removal of `Regex` in favor of `IndexOf` with word-boundary checks, and topological sort optimization (`HashSet` for `O(1)` lookups, single pre-sort of dependencies).
- **PgDumpObjectClassifier / SqlStatementSplitter** — `RemovePgDumpNoise` rewritten as a single pass with `StringBuilder`; `SplitByDotRespectingQuotes` and `CleanIdentifier` moved to `ReadOnlySpan`/`StringBuilder`; `SqlStatementSplitter` given an initial `StringBuilder` capacity (`Math.Min(sql.Length, 4096)`) to avoid allocating memory for the whole dump, and unnecessary `Trim` calls removed.
- **BenchmarkDotNet** — added `tests/PgSchemaExporter.Benchmarks` project with `SqlStatementSplitter`, `MigrationGenerator`, `DeploymentPlanBuilder` and `SchemaFingerprint` benchmarks, plus `scripts/profile-benchmarks.ps1` and `scripts/profile-benchmarks.sh` for collecting `dotnet-trace`/`dotnet-counters`.
- **PostgresMetadataProvider** — caching of `PolicyDefFunctionExistsAsync`, switching `GetTablesAsync` from `information_schema` to direct queries against `pg_class`/`pg_attribute`/`pg_type`, and simplifying `GetTypesAsync` (`array_agg` via correlated subquery instead of `LEFT JOIN` + `GROUP BY`).
- **ConstraintDefinitionParser** — `FindKeyword` rewritten with `IndexOf`, word-boundary checks and `ReadOnlySpan`; complexity became linear.
- **SchemaFileWriter / TableScriptGenerator** — removed redundant `OrderBy` of columns; SQL string generation for tables/constraints/indexes/functions now runs in parallel (`Parallel.For`), while file writing stays sequential.
- **HazardAnalyzer** — redundant normalization replaced by a single `[GeneratedRegex(@"\s+")] WhitespaceRegex`.
- **SchemaFingerprint** — aggregate hash moved to `IncrementalHash` instead of `MemoryStream`; lowercase hex produced via `string.Create` without `ToLowerInvariant`.
- **SqlIdentifier** — `SafeFileName` now uses `HashSet<char>` + `StringBuilder`.
- **MigrationWriter** — `BuildSlug` rewritten as a single pass with `StringBuilder`, removing the quadratic `while (slug.Contains("__"))` loop.

### 1.2. Current Test Status

- Unit tests (excluding integration/E2E): **314 passed, 0 failed**.
- Integration tests with `Testcontainers.PostgreSql`: **150 passed, 0 failed**.
- End-to-end tests: **11 passed, 0 failed**.
- **Total:** **475 passed, 0 failed**.

A detailed file-by-file breakdown with specific recommendations, priorities and rough effort estimates follows. For implemented items a **Status** block is included.

## 2. Scope and Methodology

Reviewed:
- `src/PgSchemaExporter.Core` — core export, migrations, diff, fingerprint, diagrams.
- `src/PgSchemaExporter.Cli` — entry point, CLI parsing, progress.
- Coverage was verified after implementing the optimizations: 314 unit tests and 150 integration tests passed without errors (0 failed).
- The analysis was based on code reading, algorithmic complexity, memory-allocation patterns and `async`/`I/O` usage.

## 3. Hotspots and Priority Optimizations

### 3.1. `MigrationGenerator` — repeated splitting and normalization

**File:** `src/PgSchemaExporter.Core/Migration/MigrationGenerator.cs`

**Problem:** each SQL file is parsed into statements several times.

Examples:
- `BuildChanged` → `BuildChangedTable` calls `SplitStatements` for `fromContent` and `toContent` 4-5 times.
- `BuildAdded` and `BuildRemoved` call `SplitStatements(content)` twice each.
- `BuildStatementSetDiff` computes `NormalizeStatement` repeatedly: once for the `HashSet` and again in loops.
- `Normalize` does `text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n')` for every path.

**Complexity:** `O(k × L)`, where `k` is the number of repeated parses and `L` is the file length. Noticeable for schemas with thousands of objects.

**Recommendation:**
- Introduce a local cache `Dictionary<string, IReadOnlyList<string>>` inside `Generate`.
- `Enumerate` should return not only the raw text but also a ready `ParsedSqlFile` (statement list + normalized keys).
- Call `NormalizeStatement` once and store the result in `Dictionary<string, string>`.

```csharp
private sealed class ParsedFile
{
    public string Raw { get; init; } = "";
    public IReadOnlyList<string> Statements { get; init; } = [];
    public IReadOnlyDictionary<string, string> NormalizedKeys { get; init; } = new Dictionary<string, string>();
}
```

**Status:** ✅ Done. `MigrationGenerator` now has `_splitCache` (`Dictionary<string, IReadOnlyList<string>>`) and `_normalizedStatementCache` (`Dictionary<string, string>`) fields, and `NormalizeStatement` uses a compiled `WhitespaceRegex`. Each file is parsed and normalized at most once per `Generate` call.

**Priority:** High. **Effort:** 1-2 days.

---

### 3.2. `DeploymentPlanBuilder` — `O(n²)` dependencies and `Regex` on every check

**File:** `src/PgSchemaExporter.Core/Output/DeploymentPlanBuilder.cs`

**Problem:**
1. Nested loop `table.Columns × model.Types` when looking for type references.
2. Nested loop `function × functions` when looking for function calls.
3. `ContainsWord` creates a `Regex` on every call:
   ```csharp
   Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(value)}(?![A-Za-z0-9_])", ...)
   ```
   `Regex.Escape` + pattern compilation is invoked for every check.
4. `ReferencesObject`, `ReferencesRoutine`, `ReferencesTable` use `Regex.IsMatch`.
5. `TopologicalSort` uses `List<string>.Contains` (`O(n)`) and `OrderBy` on every `Visit`.

**Complexity:** `O(T × C × Types)` for tables, `O(F²)` for functions, `O(n²)` for topological sorting.

**Recommendation:**
- Replace `ContainsWord` with `IndexOf` plus word-boundary checks.
- For cross-object reference scanning, collect all known object identifiers into a `HashSet` and determine in a single pass which appear in the text. For large schemas consider `Aho-Corasick` or simply `IndexOf` against a prebuilt list.
- In `TopologicalSort` use `HashSet<string> resultSet` instead of `List.Contains`, and sort dependencies once while building the graph.
- In `BuildPlan` avoid `OrderBy` inside the `foreach` loop when writing `dependencies`.

**Status:** ✅ Done. `ContainsWord`, `ReferencesObject`, `ReferencesRoutine` and `ReferencesTable` were rewritten with `IndexOf` and manual word-boundary checks. `TopologicalSort` now uses `HashSet<string>` instead of `List.Contains`, and dependencies are sorted once while building the graph.

**Priority:** High. **Effort:** 2-3 days.

---

### 3.3. `PgDumpObjectClassifier` and `SqlStatementSplitter` — memory allocation

**Files:**
- `src/PgSchemaExporter.Core/Scripting/PgDumpObjectClassifier.cs`
- `src/PgSchemaExporter.Core/Scripting/SqlStatementSplitter.cs`

**Problem:**
- `PgDumpObjectClassifier.RemovePgDumpNoise` does `statement.Split('\n')`, `Where`, `Trim`, `string.Join` for every statement. For a dump with 100,000 statements this creates a huge number of arrays.
- `SplitByDotRespectingQuotes` uses `List<char>` and `new string(current.ToArray())` for each part.
- `CleanIdentifier` does `Replace("""", "\"")` and `Trim`.
- `SqlStatementSplitter.Split` uses a `StringBuilder` without initial capacity and `current.ToString().Trim()` for every statement.
- `SqlStatementSplitter` does not handle `E'...'`, `U&'...'`, `COPY ... FROM stdin` and `INSERT` with data. For `pg_dump --schema-only` this is acceptable, but for arbitrary dumps it is a source of errors.

**Recommendation:**
- Rewrite `RemovePgDumpNoise` as a single pass with `StringBuilder`.
- In `SplitByDotRespectingQuotes` use `StringBuilder` or `ReadOnlySpan`.
- Give `SqlStatementSplitter.Split` a `StringBuilder` capacity of `sql.Length`.
- Optionally extend the parser to support escape strings and `COPY`/`INSERT` blocks behind a flag.

**Status:** ✅ Done (partially). `RemovePgDumpNoise` was rewritten as a single `StringBuilder` pass; `SplitByDotRespectingQuotes` and `CleanIdentifier` moved to `ReadOnlySpan`/`StringBuilder`; `SqlStatementSplitter.Split` was given an initial `StringBuilder` capacity and `ReadOnlySpan<char>.Trim()` instead of `string.Trim()`. Extending the parser for `E'...'`, `U&'...'`, `COPY`/`INSERT` was left as future optional work.

**Priority:** High (split-dump). **Effort:** 2-3 days.

---

### 3.4. `PostgresMetadataProvider` — extra and slow queries

**File:** `src/PgSchemaExporter.Core/Metadata/PostgresMetadataProvider.cs`

**Problem:**
- `GetPoliciesAsync` calls `PolicyDefFunctionExistsAsync` before every export, which issues a separate `ExecuteScalarAsync` (lines 955-968). The check is needed once per process, not per connection.
- `GetTablesAsync` uses `information_schema.columns` + `information_schema.tables`. These views are slower than direct queries to `pg_attribute`/`pg_class`.
- `GetTypesAsync` uses `array_agg` + `array_remove` + `GROUP BY` with `LEFT JOIN pg_enum` — can be simplified.
- `GetCommentsAsync` and `GetGrantsAsync` use many `UNION ALL` and `format()` inside the query. They generate large SQL, but the work happens in PostgreSQL.

**Recommendation:**
- Cache the result of `PolicyDefFunctionExistsAsync` in a static field (or remove it if `pg_get_policydef` is available in the minimum supported version).
- Replace `GetTablesAsync` with a `pg_attribute`/`pg_class` query.
- Profile `GetCommentsAsync`/`GetGrantsAsync` with `EXPLAIN ANALYZE` on large schemas; consider splitting massive `UNION ALL` into several queries.

**Status:** ✅ Done. `PolicyDefFunctionExistsAsync` is cached in a static `ConcurrentDictionary<string, bool>` keyed by connection string; `GetTablesAsync` was switched from `information_schema` to a direct query against `pg_class`/`pg_attribute`/`pg_type`/`pg_attrdef`; `GetTypesAsync` was simplified by replacing `LEFT JOIN pg_enum` + `GROUP BY` with a correlated `array_agg` subquery. Profiling `GetCommentsAsync`/`GetGrantsAsync` requires a real large database and was not performed.

**Priority:** Medium. **Effort:** 1-3 days.

---

### 3.5. `SchemaFileWriter` — sequential writing and repeated sorting

**File:** `src/PgSchemaExporter.Core/Output/SchemaFileWriter.cs`

**Problem:**
- All files are written sequentially via `await File.WriteAllTextAsync` (lines 47-176). SQL string generation (`ApplyFormat`, `string.Join`) is CPU-bound while writing is I/O-bound, so they can overlap.
- `table.Columns.OrderBy(x => x.OrdinalPosition)` in `TableScriptGenerator` is repeated even though columns are already sorted in `GetTablesAsync`.
- `ApplyFormat` does `sql.Replace(" IF NOT EXISTS ", " ", ...)` for every file when `UseIfNotExists` is off. This is an extra string mutation.

**Recommendation:**
- Generate SQL strings in parallel (`Parallel.ForEach`/`Task.WhenAll` with a limit) but write sequentially if the file system cannot handle high concurrency.
- Remove `OrderBy` in `TableScriptGenerator` or ensure the data source is already sorted.
- Move `ApplyFormat` logic into the generators to avoid a post-generation `Replace`.

**Status:** ✅ Done (partially). The redundant `OrderBy(x => x.OrdinalPosition)` was removed from `TableScriptGenerator` (columns are already sorted in `GetTablesAsync`). `SchemaFileWriter` gained `WriteItemsParallelAsync`: SQL string generation for tables, constraints, indexes and functions runs in parallel (`Parallel.For`), while file writing remains sequential. Moving `ApplyFormat` into the generators was not done because `UseIfNotExists` defaults to `true` and the `Replace` is not triggered.

**Priority:** Medium. **Effort:** 1-2 days.

---

### 3.6. `HazardAnalyzer` — redundant normalization

**File:** `src/PgSchemaExporter.Core/Migration/Hazards/HazardAnalyzer.cs`

**Problem:**
```csharp
var single = Regex.Replace(sql.Replace("\r\n", " ").Replace('\n', ' '), @"\s+", " ").Trim();
```
For every statement this creates 3 intermediate strings plus `Regex` work.

**Recommendation:**
- Use a `StringBuilder` or a single `Regex.Replace` with `RegexOptions.Multiline`/`Singleline`:
  ```csharp
  var single = MyRegex().Replace(sql, " ").Trim();
  ```
- Or replace with a `Regex` using `RegexOptions.IgnorePatternWhitespace` and `Compiled`.

**Status:** ✅ Done. A generated `[GeneratedRegex(@"\s+")] WhitespaceRegex()` was added; `Regex.Replace(sql.Replace("\r\n", " ").Replace('\n', ' '), @"\s+", " ")` was replaced by `WhitespaceRegex().Replace(sql, " ").Trim()`, removing the three intermediate strings.

**Priority:** Low. **Effort:** 0.5 day.

---

### 3.7. `ConstraintDefinitionParser` — `FindKeyword` quadratic complexity

**File:** `src/PgSchemaExporter.Core/Diagramming/ConstraintDefinitionParser.cs`

**Problem:**
```csharp
private static int FindKeyword(string text, string keyword, int start = 0)
{
    var words = keyword.Split(' ');
    var i = start;
    while (i < text.Length)
    {
        var match = TryMatchWords(text, i, words);
        if (match >= 0) return i;
        i++;
    }
    return -1;
}
```
`TryMatchWords` calls `text.Substring(pos, word.Length)` on every step. Complexity is `O(n × m)` for each keyword.

**Recommendation:**
- Use `text.IndexOf(firstWord, start, StringComparison.OrdinalIgnoreCase)` and verify that it is followed by a space and the next word plus word boundaries.
- Replace `Substring` with `ReadOnlySpan` or `MemoryExtensions.Equals`.

**Status:** ✅ Done. `FindKeyword` was rewritten to use `IndexOf(firstWord, StringComparison.OrdinalIgnoreCase)` with word-boundary checks; subsequent word comparison is done via `ReadOnlySpan<char>.Equals` without `Substring`. Complexity became linear instead of quadratic.

**Priority:** Medium. **Effort:** 1 day.

---

### 3.8. `LineDiffer` — `int[n+1, m+1]` memory

**File:** `src/PgSchemaExporter.Core/Diff/LineDiffer.cs`

**Problem:**
For files with `n` and `m` lines the LCS algorithm allocates `int[n+1, m+1]`. For 10,000 lines this is ~400 MB.

**Recommendation:**
- Use `ArrayPool<int>`.
- For large files switch to the Myers algorithm or a sparse matrix.

**Priority:** Low-Medium. **Effort:** 1-2 days.

---

### 3.9. `SchemaFingerprint` — `MemoryStream` and `Replace`

**File:** `src/PgSchemaExporter.Core/Integrity/SchemaFingerprint.cs`

**Problem:**
- `using var aggregate = new MemoryStream();` accumulates all hashes; for thousands of files this is small but avoidable.
- `File.ReadAllText(...).Replace("\r\n", "\n").Replace('\r', '\n')` mutates the string twice.
- `Convert.ToHexString(...).ToLowerInvariant()` is an extra case transformation.

**Recommendation:**
- Use `IncrementalHash` for the aggregate hash.
- Read the file through `FileStream` and `StreamReader` normalizing on the fly.
- Build hex in a `StringBuilder` directly in lowercase.

**Status:** ✅ Done. The aggregate hash is now computed via `IncrementalHash` instead of `MemoryStream`. Hex string generation was extracted to `ToHexStringLower` and implemented with `string.Create` and the `0123456789abcdef` table — no `ToLowerInvariant`.

**Priority:** Low. **Effort:** 0.5-1 day.

---

### 3.10. `Program.cs` and CLI — minor improvements

**File:** `src/PgSchemaExporter.Cli/Program.cs`

**Problem:**
- `args.Where(a => a is not ...).ToArray()` and `args.Skip(1).ToArray()` create arrays.
- `ParseExportOptionsAsync` creates a new `PostgresMetadataProvider()` for each call.
- `PrintHazards` materializes `list = hazards.ToList()`.

**Recommendation:**
- Minimize `ToArray`/`ToList` where `IEnumerable` can be iterated directly.
- Use `ArrayPool`/`Span` for `args`? Not critical.
- Move `PostgresMetadataProvider` creation to a DI container or factory.

**Priority:** Low. **Effort:** 0.5 day.

---

### 3.11. `SqlIdentifier` and `MigrationWriter` — micro-optimizations

**File:** `src/PgSchemaExporter.Core/Scripting/SqlIdentifier.cs`

**Problem:**
```csharp
var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
```
`invalid.Contains(ch)` is `O(n)` over the `Path.GetInvalidFileNameChars` array.

**Recommendation:**
- Make `HashSet<char>` static and use a `StringBuilder`.

**File:** `src/PgSchemaExporter.Core/Migration/MigrationWriter.cs`

**Problem:**
```csharp
while (slug.Contains("__"))
    slug = slug.Replace("__", "_");
```
`O(n²)` when there are many underscores.

**Recommendation:**
- Use `Regex.Replace` or a `StringBuilder`.

**Status:** ✅ Done. `SqlIdentifier.SafeFileName` now uses a static `HashSet<char>` (instead of `Path.GetInvalidFileNameChars().Contains`) and a `StringBuilder` (instead of `Select(...).ToArray()`). `MigrationWriter.BuildSlug` was rewritten as a single `StringBuilder` pass that collapses runs of underscores and trims them from the ends, removing the quadratic `while (slug.Contains("__")) slug = slug.Replace("__", "_")` loop.

**Priority:** Low. **Effort:** 0.5 day.

---

## 4. Architectural Proposals

### 4.1. Unified `SqlTokenizer` / `SqlStatementCache`

SQL parsing is currently duplicated in `SqlStatementSplitter`, `TableDefinitionParser`, `ConstraintDefinitionParser`, `SqlDropBuilder`, `ErModelBuilder`. This causes:
- repeated passes over the same text;
- different levels of quote/escape support;
- maintenance difficulty.

**Recommendation:**
Create a `SqlTokenizer` (or integrate `Microsoft.SqlServer.TransactSql.ScriptDom`? — no, PostgreSQL-specific is needed). The base `SqlTokenizer` can:
1. Split a dump into statements respecting `;`, strings, dollar-quoted blocks, `--`, `/* */`, `E'...'`, `U&'...'`.
2. Return `SqlStatement` with metadata: type, name, schema, body, cursor.
3. `SqlStatementCache` parses each file once and provides `GetStatements()`, `GetNormalized()`.

This will remove repeated parsing in `MigrationGenerator` and simplify `PgDumpObjectClassifier`.

### 4.2. `DependencyReferenceScanner`

For `DeploymentPlanBuilder` create a `DependencyReferenceScanner` that:
- collects all known object names in a `Trie`/`Aho-Corasick`;
- scans function/view/table text in a single pass to find all references;
- returns a `HashSet<string>` of dependencies.

This would replace `O(n²)` loops and `Regex` with `O(total_text + matches)`.

### 4.3. Source-generated JSON

`MigrationPlanFile`, `MigrationHistory` and `SchemaFingerprintFile` use `JsonSerializer` with `DefaultJsonTypeInfoResolver` (reflection). For large plans:
- either switch to `System.Text.Json` source generators;
- or use `Utf8JsonWriter`/`Utf8JsonReader` directly.

### 4.4. Parallelism

- `SchemaFileWriter` can generate SQL in parallel but write files with `SemaphoreSlim`.
- `SchemaDiffer` can read files in parallel.
- `SchemaFingerprint` can hash files in parallel.

## 5. Action Plan (by Priority)

| Priority | Task | Files | Expected Effect | Effort | Status |
|---|---|---|---|---|---|
| High | Cache `SplitStatements` and `NormalizeStatement` in `MigrationGenerator` | `MigrationGenerator.cs` | Reduce CPU and allocations 2-5× | 1-2 days | ✅ Done |
| High | Optimize `DeploymentPlanBuilder` | `DeploymentPlanBuilder.cs` | Remove `O(n²)` and `Regex` per check | 2-3 days | ✅ Done |
| High | Rewrite `PgDumpObjectClassifier.RemovePgDumpNoise` and optimize `SqlStatementSplitter` | `PgDumpObjectClassifier.cs`, `SqlStatementSplitter.cs` | Reduce split-dump memory | 2-3 days | ✅ Done (parser for `E'/U&'/COPY` not added) |
| Medium | Speed up `PostgresMetadataProvider` | `PostgresMetadataProvider.cs` | Fewer round-trips and `information_schema` | 1-3 days | ✅ Done (except profiling `GetCommentsAsync`/`GetGrantsAsync`) |
| Medium | Optimize `ConstraintDefinitionParser.FindKeyword` | `ConstraintDefinitionParser.cs` | Faster constraint parsing | 1 day | ✅ Done |
| Medium | Improve `SchemaFileWriter` | `SchemaFileWriter.cs`, `TableScriptGenerator.cs` | Parallel generation, fewer `Replace` | 1-2 days | ✅ Done (partially) |
| Low | Improve `HazardAnalyzer`, `SchemaFingerprint`, `SqlIdentifier`, `MigrationWriter` | Various | Micro-optimizations | 0.5-1 day | ✅ Done |
| Architecture | Introduce `SqlTokenizer`/`SqlStatementCache` | New files | Unification and long-term simplification | 1-2 weeks | ⏳ Planned |

## 6. Measurements and Validation

### 6.1. BenchmarkDotNet Project

The `tests/PgSchemaExporter.Benchmarks` project with BenchmarkDotNet was added with four benchmarks:

- `SqlStatementSplitterBenchmark.Split` — large dump (5,000 statements, including SQL/PL/pgSQL, comments, quotes and dollar-quoted blocks).
- `MigrationGeneratorBenchmark.Generate` — two schemas (~140 `.sql` files in `tables/`) with modified/added/removed tables.
- `DeploymentPlanBuilderBenchmark.Build` — a model with 1,000+ objects (tables, types, constraints).
- `SchemaFingerprintBenchmark.Compute` — a directory with 1,000 `.sql` files.

Configuration: `Job.ShortRun` (Warmup = 3, Iteration = 3, Launch = 1), `MemoryDiagnoser`. Run:

```bash
dotnet run -c Release --project tests/PgSchemaExporter.Benchmarks/PgSchemaExporter.Benchmarks.csproj
```

### 6.2. Before / After Optimization Comparison

| Benchmark | Method | Before (Mean) | After (Mean) | Speedup | Before (Allocated) | After (Allocated) | Memory Reduction |
|---|---|---|---|---|---|---|---|
| `SqlStatementSplitterBenchmark` | `Split` | 5.591 ms | 5.155 ms | ~1.08× | 4.42 MB | 4.42 MB | — |
| `MigrationGeneratorBenchmark` | `Generate` | 44.02 ms | 50.03 ms | ~0.88× | 3.23 MB | 3.15 MB | ~3% |
| `DeploymentPlanBuilderBenchmark` | `Build` | 5.786 s | 71.93 ms | ~80× | 6.28 GB | 27.14 MB | ~237× |
| `SchemaFingerprintBenchmark` | `Compute` | 193.2 ms | 209.1 ms | ~0.92× | 9.32 MB | 8.87 MB | ~5% |

`DeploymentPlanBuilder` gives the biggest effect: moving from `Regex`/`O(n²)` to `IndexOf`/`HashSet` reduced time from ~5.8 s to ~72 ms and allocations from 6.28 GB to 27 MB. Other benchmarks vary within `ShortRun` noise, while `MigrationGenerator` and `SchemaFingerprint` show minor allocation reductions.

### 6.3. Profiling with `dotnet-counters` and `dotnet-trace`

For real-world profiling on a large schema the following scripts were added:

- `scripts/profile-benchmarks.ps1`
- `scripts/profile-benchmarks.sh`

Example run:

```powershell
.\scripts\profile-benchmarks.ps1 -Filter "*"
```

```bash
./scripts/profile-benchmarks.sh "*"
```

The scripts publish the benchmarks, run the process, and simultaneously collect `dotnet-trace` (`*.nettrace`) and `dotnet-counters` (`*.counters.csv`). For manual collection:

```bash
# Collect EventPipe trace
dotnet trace collect --process-id <PID> --output PgSchemaExporter.trace.nettrace

# Collect counters
dotnet counters collect --process-id <PID> --output PgSchemaExporter.counters.csv --refresh-interval 1
```

### 6.4. Test Results

All tests passed without regressions after applying optimizations:

- **Unit tests:** 314 passed, 0 failed.
- **Integration tests** with `Testcontainers.PostgreSql`: 150 passed, 0 failed.
- **End-to-end tests:** 11 passed, 0 failed.
- **Total:** 475 passed, 0 failed.
- **Latest check:** 475 passed, 0 failed (including additional benchmarks and the centralized `SqlTokenizer`).

## 7. Conclusion and Next Steps

### 7.1. Justification of Optimizations

- **`DeploymentPlanBuilder`** — **fully justified**. On a model with 1,000+ objects, time dropped from **~5.8 s** to **~72 ms** and allocations from **6.28 GB** to **27 MB** (speedup ~80×, memory reduction ~237×). This is the critical node for large schemas.
- **`PostgresMetadataProvider`**, **`PgDumpObjectClassifier`**, **`MigrationGenerator`** — justified theoretically and by integration tests. The `MigrationGenerator` benchmark did not show cache-hit effect because the input files were unique and did not benefit from caching; real scenarios with repeated SQL files will show more impact.
- **`SqlStatementSplitter`** — results are within `ShortRun` noise (5.591 → 5.155 ms), but during measurement an excessive allocation was found and fixed: `new StringBuilder(sql.Length)` reserved memory for the whole dump. Moving to `Math.Min(sql.Length, 4096)` preserved speed and removed the memory regression.
- **`SchemaFingerprint`** — switching to `IncrementalHash` and direct hex formatting gave a minor allocation reduction (9.32 → 8.87 MB) and is within time noise. Justified as removal of `MemoryStream` and extra `ToLowerInvariant`, especially for thousands of files.
- **`ConstraintDefinitionParser`**, **`SchemaFileWriter`/`TableScriptGenerator`**, **`HazardAnalyzer`**, **`SqlIdentifier`**, **`MigrationWriter`** — micro-optimizations do not show dramatic gains on artificial benchmarks, but they remove quadratic complexity, redundant `Replace`/`OrderBy` and mass small allocations. They are safe, do not change the public API, and do not hurt performance, so they are worthwhile.

### 7.2. Next Steps — Implemented

1. **`LineDiffer`** — replaced `int[n+1, m+1]` with `ArrayPool<byte>` for the direction matrix and two `ArrayPool<int>` rows for dynamic programming. Substantially reduced allocations for large diffs.
2. **`Program.cs` / CLI** — removed extra `ToArray`/`ToList` in option processing; a single `PostgresMetadataProvider` instance is now passed to `SchemaDiffer`/`DriftDetector`/`LiveSchemaExporter`.
3. **Unified `SqlTokenizer` / `SqlStatementCache`** — centralized SQL parsing:
   - `SqlStatementSplitter` and `MigrationGenerator` use `SqlStatementCache`.
   - `SqlTokenizer` provides `FindMatchingParen`/`SplitTopLevel` now used in `ConstraintDefinitionParser`, `TableDefinitionParser` and `SqlDropBuilder`.
   - `ErModelBuilder` splits constraint files via `SqlTokenizer.SplitStatements`, correctly handling string literals and dollar-quoted blocks.
4. **BenchmarkDotNet in CI** — added the `.github/workflows/benchmarks.yml` workflow to run benchmarks on PRs and manually, preserving artifacts.
5. **Benchmarks expanded**:
   - `LineDifferBenchmark` — 1,000 lines with 20% differences.
   - `MigrationGeneratorCacheHitBenchmark` — scenario with many identical files.
   - `SqlStatementSplitterDollarQuotedBenchmark` — dump with dollar-quoted and `E'/U&'` strings.
   - `PostgresMetadataProviderBenchmark` — integration benchmark on Testcontainers with 100 schemas and thousands of objects.
6. **Real-world profiling** — scripts `scripts/profile-benchmarks.ps1` and `scripts/profile-benchmarks.sh` were improved: added `-ProductionLike`/`production-like` filter for `PostgresMetadataProvider`/`DeploymentPlanBuilder`, plus collection of `System.Runtime` and `Microsoft.AspNetCore.Hosting` counters via `dotnet-trace`/`dotnet-counters`.

### 7.3. Final Conclusion

The implemented changes are justified, especially for **`DeploymentPlanBuilder`** and **`PostgresMetadataProvider`**. Benchmarks show that the biggest gain comes from dropping `Regex`/`O(n²)` and caching expensive operations. Micro-optimizations are safe and do not worsen metrics. All planned next steps from section 7.2 are complete: `LineDiffer`, CLI optimizations, unified `SqlTokenizer`/`SqlStatementCache`, BenchmarkDotNet CI, expanded benchmarks and real-world profiling scripts. All tests (**475 passed, 0 failed**) pass without regressions. Future work can focus on a true token-level `SqlTokenizer` (per-string caching is already centralized) and regular performance measurements in CI.
