# Release Notes — pgschema-export v2.1.0

**Release date:** 2026-07-18

## Highlights

- **Token-level SQL parser unification** — `SqlTokenizer` now exposes a full token stream API (`SqlToken`, `SqlTokenKind`, `Tokenize`, `FindKeyword`, `ReadIdentifier`, `ReadNameAfter`, `ReadParenthesized`). `TableDefinitionParser`, `ConstraintDefinitionParser`, and `SqlDropBuilder` have been migrated to use it, removing duplicated string-scanning logic for quotes, comments, and dollar-quoted strings.
- **Parser unit tests** — new `SqlTokenizerTests` exercise tokenization, keyword detection, identifier reading, and parenthesized expression parsing without requiring Docker.
- **Migration journal & resume** — `MigrationApplier` writes each applied statement to a `pgschema_migration_journal` table and `apply --resume` safely skips already-applied statements from a partial or retried run.
- **Production-safe progress reporting** — `ConsoleProgressReporter` is now thread-safe, so `--parallel` export/diff no longer corrupts console output.
- **CI benchmark regression detection** — `.github/workflows/benchmarks.yml` compares PR results against a stored baseline and fails on statistically significant regressions.
- **.NET global tool packaging** — `PgSchemaExporter.Cli` is now packable as a .NET global tool and can be installed with `dotnet tool install -g PgSchemaExporter.Cli`.
- **Source-generated config serialization** — `ExportConfigLoader` / `ExportConfigWriter` now use `PgSchemaExporterJsonContext` for AOT/trimming-compatible JSON handling.
- **Metadata cache correctness** — `PolicyDefFunctionExistsCache` is no longer static/stale and invalidates correctly when the connection/schema context changes.

---

## New Features

- **SQL tokenizer API**
  - `SqlTokenizer.Tokenize(sql)` returns a read-only list of `SqlToken` values.
  - Token kinds: `Whitespace`, `LineComment`, `BlockComment`, `StringLiteral`, `QuotedIdentifier`, `DollarQuoted`, `Word`, `Symbol`, `Other`.
  - Helpers: `FindKeyword`, `ReadNameAfter`, `ReadIdentifier`, `ReadParenthesized`, `SkipTrivia`, `SkipLeadingNoise`.
  - Correctly handles nested parentheses inside strings, quoted identifiers with escaped quotes, and dollar-quoted function bodies.
- **Parser migration**
  - `TableDefinitionParser` tokenizes `CREATE TABLE` statements to extract schema, name, columns, and inline constraints.
  - `ConstraintDefinitionParser` reads primary-key, unique, check, exclusion, and foreign-key constraints from token streams.
  - `SqlDropBuilder` generates `DROP` and `REVOKE` statements using token-based name extraction and keyword detection.
- **Migration journal**
  - `MigrationApplier` automatically creates `pgschema_migration_journal` (name configurable with `--journal-table`).
  - Records plan name, direction, sequence index, statement hash, SQL, and `applied_at` timestamp.
  - `apply --resume` loads the journal and skips statements that were already applied.
- **`apply --resume` CLI option**
  - `pgschema-export apply --plan plan.json --resume --connection "<conn>"` resumes a previous run safely.
- **Benchmark regression detection**
  - Baseline `BenchmarkDotNet` results stored on `main` and compared on PRs.
  - Fails the workflow if mean time regresses > 10 % or allocated memory regresses > 20 %.
- **.NET global tool**
  - `PackAsTool` metadata added to `PgSchemaExporter.Cli.csproj`.
  - Tool command name: `pgschema-export`.

---

## Improvements

- **Thread-safe console progress**
  - `ConsoleProgressReporter` uses `Interlocked` and a lock around console output, preventing torn/corrupt progress lines under `--parallel`.
- **Config serialization**
  - `ExportConfigLoader` / `ExportConfigWriter` moved from reflection-based `DefaultJsonTypeInfoResolver` to `PgSchemaExporterJsonContext`.
- **Cache correctness**
  - `PolicyDefFunctionExistsCache` is now instance-scoped and invalidated when the metadata provider context changes.
- **Dry-run reliability**
  - `MigrationApplier` no longer opens a database connection when `--dry-run` is specified; journal and resume logic are bypassed for preview runs.

---

## Bug Fixes

- Fixed token-based parsing edge cases in `TableDefinitionParser` for quoted identifiers and column clauses (`NOT NULL`, `DEFAULT`, `COLLATE`, `GENERATED`).
- `ConstraintDefinitionParser` now correctly skips comments and dollar-quoted strings while locating constraint keywords.
- `SqlDropBuilder` handles `IF NOT EXISTS`, `ONLY`, and `CONCURRENTLY` noise words before object names.

---

## Performance

- SQL tokenization hot paths remain allocation-efficient and are now shared across three parsers.
- `SqlStatementCache` continues to cache tokenized results for repeated parsing.

---

## Tests

- **487 tests, 0 failures** across the full `dotnet test PgSchemaExporter.slnx` run.
- New unit tests:
  - `SqlTokenizerTests` — tokenization and helper coverage.
  - Updated `TableDefinitionParserTests`, `DiagrammingTests`, and `SqlDropBuilderTests` to exercise the token-based paths.
- Integration tests continue to cover `plan`/`apply`, `drift`, `diff`, `export`, `migrate`, `fingerprint`, and `watch` workflows.

---

## Documentation

- Updated `README.md` to reflect v2.1.0 features and roadmap.

---

## Migration Guide

1. No CLI invocation changes are required; existing commands and arguments continue to work.
2. If you call `MigrationApplier` programmatically with `DryRun = true`, the applier no longer opens a connection.
3. To publish the global tool: `dotnet pack src/PgSchemaExporter.Cli/PgSchemaExporter.Cli.csproj -c Release` and push the resulting `.nupkg` to a NuGet feed.

---

## Verification

```bash
dotnet build PgSchemaExporter.slnx -c Release
dotnet test PgSchemaExporter.slnx -c Release
```

Both commands complete successfully.
