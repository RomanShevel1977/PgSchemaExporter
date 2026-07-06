# Release Notes v1.4.0

## Developer Experience

This release focuses on developer ergonomics: a config scaffolding command, a live watch loop, an HTML diff report, and optional parallel metadata extraction.

### `init` command

Scaffold a `pgschema-export.json` configuration file:

```bash
pgschema-export init
pgschema-export init --output ./config/pgschema-export.json --force
```

- Writes a fully-populated template (connection string placeholder, schemas, include flags, format options).
- Refuses to overwrite an existing file unless `--force` is provided.
- The generated file can be consumed via `pgschema-export export --config pgschema-export.json`.

### `watch` command

Continuously re-run a directory diff and print an updated report whenever any `.sql` file changes under either tree:

```bash
pgschema-export watch --left ./old-schema --right ./new-schema
pgschema-export watch --left ./old-schema --right ./new-schema --format html
```

- Prints an initial diff, then recomputes on file-system events.
- Events are debounced so a burst of writes triggers a single recomputation.
- Directory-to-directory only (live databases are not watchable); stop with `Ctrl+C`.

### HTML diff report

The `diff` command now supports an `html` format in addition to `text` and `json`:

```bash
pgschema-export diff --left ./old --right ./new --format html --output diff.html
pgschema-export diff --left ./old --right ./new --output diff.html   # inferred from extension
```

- Self-contained, styled HTML page (dark theme) with added/removed/changed sections and a summary.
- When `--format` is omitted, the format is inferred from the `--output` extension (`.html`/`.htm` → HTML, `.json` → JSON).

### Parallel export

Large databases can be exported faster by running metadata queries concurrently:

```bash
pgschema-export export --connection "<conn>" --output ./db-schema --parallel
```

- Each query runs on its own pooled connection (Npgsql allows one active command per connection).
- Concurrency is bounded (max 8 simultaneous queries) to avoid exhausting server connection limits.
- Also configurable via `"parallel": true` in `pgschema-export.json`.
- Sequential mode remains the default; results are identical.

### Fixes

- `--version` now reports the correct version (previously hard-coded to `1.1.0`).

### Tests

- Added coverage for the HTML diff report, config template generation, and the watch loop.

### Compatibility

- Requires .NET 8.0 and Npgsql 8.0.5.
- Backward compatible with 1.3.x; all new behavior is opt-in.
