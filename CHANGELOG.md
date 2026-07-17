# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.0.0] - 2026-07-17

### Added

- **Solution file and version unification**
  - Added `PgSchemaExporter.slnx` to unify all projects.
  - Unified product version to `2.0.0` across `PgSchemaExporter.Cli.csproj` and `PgSchemaExporter.Core.csproj`.

- **CLI command handler architecture**
  - New `src/PgSchemaExporter.Cli/Commands/` folder containing the `ICommand` abstraction and dedicated command handlers:
    - `ApplyCommand.cs`
    - `CommandContext.cs`
    - `CommandDispatcher.cs`
    - `DiagramCommand.cs`
    - `DiffCommand.cs`
    - `DriftCommand.cs`
    - `ExportCommand.cs`
    - `FingerprintCommand.cs`
    - `ICommand.cs`
    - `InitCommand.cs`
    - `MigrateCommand.cs`
    - `PlanCommand.cs`
    - `SplitDumpCommand.cs`
    - `WatchCommand.cs`
  - `CommandDispatcher` resolves commands by name and passes a shared `CommandContext` (args, progress, logger, metadata provider, cancellation token).
  - `Program.cs` is now a minimal bootstrap that delegates all command logic to `CommandDispatcher`.

- **Migration history atomic writes**
  - `MigrationHistory.AppendAsync` now writes the `history.json` file atomically using `FileStream` with `FileShare.None` and a temporary-file/replace pattern.

- **Command timeout and journal for `MigrationApplier`**
  - Added `CommandTimeoutSeconds` to `MigrationApplier.ApplyOptions`.
  - Individual `NpgsqlCommand` instances now respect the configured command timeout.
  - `MigrationApplier` now records a `Journal` of executed, skipped, and failed statements in the apply result.

- **Source-generated JSON context support**
  - `ExportOptions`, `IncludeOptions`, and `FormatOptions` are now serializable via `PgSchemaExporterJsonContext`.
  - `ExportConfigLoader` and `ExportConfigWriter` use `PgSchemaExporterJsonContext.Default`.

- **Integration and unit tests**
  - Added integration tests covering CLI parsing, diff, dump splitting, schema export, migration generation, fingerprinting, plan/apply flow, and schema watching.
  - Added unit tests for `HazardAnalyzer`, `MigrationApplier`, `PgDumpObjectClassifier`, `SqlDropBuilder`, `SqlStatementSplitter`, and `TableDefinitionParser`.
  - Added `TestResults/PgSchemaExporter.Tests.trx` and removed the stale `IntegrationTestResults.trx`.

- **Benchmarks**
  - Added `PgSchemaExporter.Benchmarks` project with benchmarks for:
    - `DeploymentPlanBuilder`
    - `LineDiffer`
    - `MigrationGenerator`
    - `PostgresMetadataProvider`
    - `SchemaFingerprint`
    - `SqlStatementSplitter`
    - `SqlStatementSplitterDollarQuoted`
  - Added benchmark reports, `benchmark-comparison.md`, and helper scripts (`scripts/Compare-Benchmarks.ps1`, `scripts/profile-benchmarks.ps1/sh`, `scripts/run-unit-tests.ps1/sh`).

### Changed

- **Export options normalization (P1)**
  - `ExportOptions.EnsureValidForExport` no longer mutates the `Schemas` array.
  - New computed `EffectiveSchemas` property provides trimmed, deduplicated, non-empty schema names.
  - `PostgresMetadataProvider.AddSchemaParameters` uses `options.EffectiveSchemas` and `options.ExcludeSchemas`.

- **SQL identifier escaping**
  - `ParsedTable.QualifiedName` now uses `SqlIdentifier.Quote` and `SqlIdentifier.Qualified` for safe quoting.
  - `TableMigrationBuilder` uses the quoted `QualifiedName` directly and removed the redundant `FormatTableName` helper.
  - `MigrationScript.AppendSessionSettings` escapes `lock_timeout` and `statement_timeout` with `SqlLiteral.String`.
  - `MigrationApplier` also escapes timeout values when applying session settings.

- **Dependency injection consistency**
  - `SchemaExporter` and `DumpSplitter` now expose constructors that allow full injection of `DependencyManifestWriter` and `DeploymentPlanBuilder`.

- **Streaming dump reads**
  - `DumpSplitter.SplitAsync` reads the input file through a `FileStream`/`StreamReader` instead of `File.ReadAllTextAsync`.

- **Concurrency in schema diffing**
  - `SchemaDiffer` now uses `ConcurrentBag<T>` for parallel collection of added/removed/changed entries.
  - `BuildStatistics` accepts `IEnumerable<string>` to support the concurrent collections.

- **Documentation**
  - Updated `README.md` and `doc/USAGE_GUIDE.md`.
  - Added `doc/Performance_Refactoring_Report.md` and `doc/Performance_Refactoring_Report_Full.md` (English originals).

### Fixed

- **Migration generator performance**
  - Multiple performance improvements to `MigrationGenerator` and related parsing paths.
  - Optimized `SqlTokenizer.IndexOfWord`, `MatchesWordAt`, `LastIndexOfWord`, and `ReadIdentifier` to reduce allocations and add fast paths for single-word searches.
  - `SqlTokenizer.UnquoteQualified` short-circuits when no quotes are present.

- **Schema diffing and reporting**
  - `SchemaDiffReportWriter` and `SchemaDiffer` reliability fixes.
  - `LineDiffer` and `LiveSchemaExporter` tuning.
  - `SchemaDiagramGenerator`, `ConstraintDefinitionParser`, and `ErModelBuilder` improvements.

- **Migration hazards and DDL**
  - `HazardAnalyzer`, `SqlDropBuilder`, `OnlineDdlRewriter`, and `TableDefinitionParser` fixes.
  - `MigrationTimeout` handling refined.

- **Fingerprinting**
  - `SchemaFingerprint` and `SchemaFingerprintFile` updated for robustness.

### Tests

- Full test suite passed: **475 tests, 0 failures**.

### Commits since `v1.9.0`

```
50c4a16 2026-07-17 refactor: implement prioritized recommendations and split CLI into ICommand handlers
f78a82f 2026-07-17 performance of MigrationGenerator fixed
ad70ca2 2026-07-17 performance optimization
6438eee 2026-07-17 update docs
4b6ddaf 2026-07-17 Update README.md
27b464f 2026-07-16 benchmark reports
0e642f6 2026-07-16 performance optimization
267bd66 2026-07-16 Delete IntegrationTestResults.trx
a8e7c74 2026-07-15 added integration tests
3610385 2026-07-14 added integration tests
```

### Files changed since `v1.9.0`

- 173 files changed, approximately `+22,053 / -3,084` lines.
- Key directories: `src/PgSchemaExporter.Cli/`, `src/PgSchemaExporter.Core/`, `tests/PgSchemaExporter.Tests/`, `tests/PgSchemaExporter.Benchmarks/`, `doc/`, `scripts/`.

### Untracked files pending inclusion

```
ASSESSMENT_REPORT.md
doc/Performance_Refactoring_Report.ru.md
doc/Performance_Refactoring_Report_Full.ru.md
```

## Release History

- [v1.9.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.9.0)
- [v1.8.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.8.0)
- [v1.7.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.7.0)
- [v1.6.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.6.0)
- [v1.5.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.5.0)
- [v1.4.1](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.4.1)
- [v1.4.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.4.0)
- [v1.3.1](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.3.1)
- [v1.3.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.3.0)
- [v1.2.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.2.0)
- [v1.1.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.1.0)
- [v1.0.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v1.0.0)
- [v0.9.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.9.0)
- [v0.8.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.8.0)
- [v0.7.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.7.0)
- [v0.6.1](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.6.1)
- [v0.6.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.6.0)
- [v0.5.1](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.5.1)
- [v0.5.0](https://github.com/RomanShevel1977/PgSchemaExporter/releases/tag/v0.5.0)
