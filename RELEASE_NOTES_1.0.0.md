# Version 1.0.0 Release Notes

## Planned direction
- Stabilize the export workflow for real PostgreSQL deployments.
- Improve SQL generation quality for tables, views, functions, and constraints.
- Strengthen dependency ordering and deployment safety.
- Expand automated test coverage and diagnostics.

## Current milestone improvements
- Added initial support for identity columns and collations in generated table definitions.
- Added richer CLI diagnostics for completed exports and errors, including per-object counts.
- Implemented a real `--dry-run` option that connects and reports what would be exported without writing files.
- Added a `diff` command that compares two exported schema directories and reports added/removed/changed objects (exit code `2` on differences).
- Wired `format` options (`useIfNotExists`, `splitConstraints`, `splitIndexes`) into the file writer; constraints and indexes can now be inlined into table files.
- Exposed `--include-<kind>` / `--exclude-<kind>` CLI flags for every object kind, matching the config file.

## Breaking changes
- Removed the unused `format.uppercaseKeywords` configuration option.
