# Release Notes — pgschema-export v2.0.0

**Release date:** 2026-07-17

## Highlights

- **CLI architecture rebuilt** — `Program.cs` is now a thin bootstrap; every command is implemented as an `ICommand` handler in `src/PgSchemaExporter.Cli/Commands/`.
- **Security & correctness** — SQL identifiers and `SET lock_timeout/statement_timeout` values are now safely escaped/quoted.
- **Atomic history writes** — `MigrationHistory.AppendAsync` no longer risks corruption on concurrent writes.
- **Performance** — `SqlTokenizer`, `MigrationGenerator`, `SchemaDiffer`, and `DumpSplitter` all received targeted optimizations.
- **Testing** — 475 integration and unit tests pass; new benchmark suite added.
- **Solution file** — all projects are now unified under `PgSchemaExporter.slnx` with a single `2.0.0` version.

---

## Breaking Changes

- `Program.cs` no longer contains command logic; downstream callers or forks that depended on `Program` internals will need to use the new `ICommand` / `CommandDispatcher` API.
- `ExportOptions.EnsureValidForExport()` no longer mutates the `Schemas` array. Use the new `EffectiveSchemas` property for normalized, deduplicated schema names.
- `MigrationApplier.ApplyOptions` now includes `CommandTimeoutSeconds`; existing call sites may want to set an explicit value (default remains `0` / driver default).

## New Features

- **Command handlers for every CLI command**
  - `ApplyCommand`, `DiagramCommand`, `DiffCommand`, `DriftCommand`, `ExportCommand`, `FingerprintCommand`, `InitCommand`, `MigrateCommand`, `PlanCommand`, `SplitDumpCommand`, `WatchCommand`
  - Shared `CommandContext` carries args, progress, logger, metadata provider, and cancellation token.
- **Atomic migration history**
  - `MigrationHistory.AppendAsync` writes to a temporary file and performs an atomic replace, preventing partial/corrupt `history.json` files.
- **Command timeout & journal in `MigrationApplier`**
  - Configurable `CommandTimeoutSeconds` per statement.
  - Apply result includes a journal of executed, skipped, and failed statements.
- **Source-generated JSON serialization**
  - `ExportOptions`, `IncludeOptions`, and `FormatOptions` are now part of `PgSchemaExporterJsonContext`.
  - `ExportConfigLoader` / `ExportConfigWriter` use the source-generated context for faster, trim-safe serialization.
- **Streaming dump reads**
  - `DumpSplitter` now reads the input SQL dump through a `FileStream`/`StreamReader` instead of loading the entire file into memory.
- **Solution & version unification**
  - New `PgSchemaExporter.slnx`.
  - `PgSchemaExporter.Cli` and `PgSchemaExporter.Core` versions bumped to `2.0.0`.

## Improvements

- **Export options normalization**
  - `EnsureValidForExport` only validates.
  - `EffectiveSchemas` provides trimmed, deduplicated, non-empty schema names.
  - `PostgresMetadataProvider` queries use `EffectiveSchemas` and `ExcludeSchemas`.
- **SQL identifier escaping**
  - `ParsedTable.QualifiedName` uses `SqlIdentifier.Quote` / `SqlIdentifier.Qualified`.
  - `TableMigrationBuilder` uses the quoted name directly.
  - `SET lock_timeout` / `SET statement_timeout` values are escaped with `SqlLiteral.String`.
- **Concurrency in `SchemaDiffer`**
  - Replaced `List<T>` + `lock` with `ConcurrentBag<T>` in parallel diff paths.
- **DI consistency**
  - `SchemaExporter` and `DumpSplitter` expose constructors that accept `DependencyManifestWriter` and `DeploymentPlanBuilder`.
- **Tokenizer performance**
  - `IndexOfWord`, `MatchesWordAt`, `LastIndexOfWord`, `ReadIdentifier`, and `UnquoteQualified` have fast paths and reduced allocations.

## Bug Fixes

- `MigrationGenerator` performance and correctness fixes.
- `SqlDropBuilder`, `OnlineDdlRewriter`, `TableDefinitionParser`, and `HazardAnalyzer` refinements.
- `SchemaDiffReportWriter`, `LineDiffer`, and `LiveSchemaExporter` reliability improvements.
- `SchemaFingerprint` / `SchemaFingerprintFile` robustness updates.
- `SchemaDiagramGenerator`, `ConstraintDefinitionParser`, and `ErModelBuilder` fixes.

## Performance

- Added `PgSchemaExporter.Benchmarks` project covering hot paths: `DeploymentPlanBuilder`, `LineDiffer`, `MigrationGenerator`, `PostgresMetadataProvider`, `SchemaFingerprint`, `SqlStatementSplitter`.
- `MigrationGenerator` optimized across multiple commits.
- `SqlTokenizer` hot paths optimized for single-word lookups and unquoted identifiers.

## Tests

- **475 tests, 0 failures** across the full `dotnet test PgSchemaExporter.slnx` run.
- New integration tests for CLI, diff, dump splitting, export, migration, fingerprint, plan/apply, and schema watching.
- New unit tests for `HazardAnalyzer`, `MigrationApplier`, `PgDumpObjectClassifier`, `SqlDropBuilder`, `SqlStatementSplitter`, and `TableDefinitionParser`.

## Documentation

- Updated `README.md` and `doc/USAGE_GUIDE.md`.

## Migration Guide

1. Update any code reading `ExportOptions.Schemas` after validation to use `ExportOptions.EffectiveSchemas`.
2. If you call `MigrationApplier` programmatically, set `CommandTimeoutSeconds` if the driver default is insufficient.
3. No CLI invocation changes are required; all existing command-line arguments continue to work.

## Verification

```bash
dotnet build PgSchemaExporter.slnx
dotnet test PgSchemaExporter.slnx
```

Both commands complete successfully.
