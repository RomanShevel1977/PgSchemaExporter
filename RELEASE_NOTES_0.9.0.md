# Version 0.9.0 Release Notes

## New
- Added dependency-aware deployment ordering for schema objects.
- Added `--version` / `-v` to the CLI.
- Added stronger validation for export configuration and config files.
- Added a more helpful generated README in output directories.

## Behavior
- The deployment plan now considers dependencies between schema objects, tables, views, functions, triggers, policies, comments, and grants.
- Export validation now fails earlier with clearer errors for missing connection strings, output directories, or invalid schemas.

## Packaging
- Release scripts now target version 0.9.0 artifacts for Windows, Linux, and macOS.
