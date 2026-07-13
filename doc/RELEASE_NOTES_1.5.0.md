# Release Notes v1.5.0

## Developer Experience Enhancements

This release makes the tool easier to operate and debug: structured logging, live progress for long-running operations, verbosity controls, actionable error messages, and thorough configuration validation.

### Verbosity flags

Two new global flags control how much information is printed:

```bash
pgschema-export export --connection "<conn>" --output ./db-schema --verbose
pgschema-export export --connection "<conn>" --output ./db-schema --quiet
```

- `--verbose` — prints per-object-kind progress with a running counter plus debug logs.
- `--quiet` — suppresses progress; only errors are shown.
- Default (neither flag) — shows high-level start/finish milestones.
- If both are supplied, `--quiet` wins.

All diagnostic output is written to **stderr**, so machine-readable results on stdout (for example `diff --format json`) stay clean and pipeable.

### Progress reporting

Long-running operations now report progress:

- `export` reports metadata loading (per object kind in `--verbose`), then the write/plan/deploy stages.
- `diff` against a live database reports each side's export and the comparison step.
- A total elapsed time is reported when metadata loading completes.

Progress is driven by a new `IProgressReporter` abstraction in the core library; the CLI supplies a `ConsoleProgressReporter` that honors the selected verbosity.

### Structured logging

The core library now accepts an `ILogger` (`Microsoft.Extensions.Logging.Abstractions`) throughout the export and diff paths:

- Object-kind counts and load timings are logged at `Debug`/`Information`.
- Failures are logged with the full exception at `Error`.
- Library consumers can pass their own `ILogger`; when omitted, a no-op logger is used.

### Actionable error messages

Errors now include a concise headline plus a targeted suggestion via the new `FriendlyError` helper. Examples:

- Authentication failures (`28P01`) → "Check the Username/Password in your connection string."
- Missing database (`3D000`) → "Check the 'Database' value in your connection string."
- Unreachable host (`SocketException`) → "Verify Host/Port and that PostgreSQL is running and accepting TCP connections."
- Too many connections (`53300`) → "Reduce --parallel concurrency or increase the server's max_connections."
- Invalid arguments → "Run 'pgschema-export --help' to review the expected arguments."

### Configuration validation

`export --config` now validates the configuration file up front and reports **all** problems at once:

- Missing file → a clear message pointing to `pgschema-export init`.
- Empty or non-object JSON → an explicit error.
- Malformed JSON → the parse error with line/position information.
- Semantic issues (missing connection string, empty schemas, etc.) are collected into a single `ConfigValidationException` with one actionable line per problem.

`ExportOptions.Validate()` is available for library consumers that want to check options without throwing on the first error.

### Tests

- Added `ConfigValidationTests` covering missing/empty/malformed/invalid configs and multi-error collection.
- Added `FriendlyErrorTests` covering the exception-to-suggestion mapping.
- Updated the metadata provider stub to the new `LoadAsync` signature.

### Compatibility

- Requires .NET 8.0, Npgsql 8.0.5, and `Microsoft.Extensions.Logging.Abstractions` 8.0.2.
- Backward compatible with 1.4.x. The new `IProgressReporter`/`ILogger` parameters are optional; existing calls and configs work unchanged.
