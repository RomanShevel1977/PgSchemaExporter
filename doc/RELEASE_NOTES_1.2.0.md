# Release Notes v1.2.0

## Live-to-Live Diff & CI/CD

This release adds CI/CD integration capabilities with live database comparison and machine-readable JSON output.

### New Features

- **Live database diff** — Compare schemas directly from live PostgreSQL databases using `--left-db` and `--right-db` options
- **JSON output format** — Use `--format json` to get machine-readable diff output for CI pipelines
- **GitHub Action workflow** — Official workflow template for schema diff in PRs (`.github/workflows/schema-diff.yml`)
- **Exit code documentation** — Documented exit codes for CI gating (0=success, 1=error, 2=differences)

### CLI Changes

#### Diff command new options:
- `--left-db <connection-string>` — Left (baseline) live PostgreSQL connection string
- `--right-db <connection-string>` — Right (target) live PostgreSQL connection string
- `--format <text|json>` — Output format (default: text)

#### Examples:

```bash
# Compare live DB against exported directory
pgschema-export diff --left-db "Host=localhost;Database=prod" --right ./db-schema

# Compare two live databases
pgschema-export diff --left-db "Host=localhost;Database=staging" --right-db "Host=localhost;Database=prod"

# JSON output for CI
pgschema-export diff --left ./db-schema --right ./db-schema-live --format json --output diff.json
```

### GitHub Action

The included workflow automatically:
- Sets up a PostgreSQL service container
- Exports schema from the live database
- Compares against the committed schema directory
- Fails the PR if schema changes are detected
- Uploads the diff report as an artifact

### Exit Codes

- **0** — Success (no differences detected)
- **1** — Error (invalid arguments, missing files, connection failure)
- **2** — Differences detected (used by `diff` command)

### Internal Changes

- Added `LiveSchemaExporter` class to export live DB to temporary directories
- Extended `SchemaDiffer` with `DiffAsync` method for live database comparisons
- Extended `SchemaDiffOptions` with connection string properties and `DiffFormat` enum
- Extended `SchemaDiffReportWriter` with JSON output support
- Added comprehensive tests for new diff options and JSON output

### Compatibility

- Requires .NET 8.0
- Requires Npgsql 8.0.5
- Backward compatible with existing directory-to-directory diff functionality
