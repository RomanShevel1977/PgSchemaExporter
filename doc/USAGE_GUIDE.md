# PgSchemaExporter Usage Guide

Complete guide to the `pgschema-export` utility for exporting, comparing, migrating, and validating PostgreSQL schemas.

## Table of Contents

1. [Overview](#overview)
2. [Installation](#installation)
3. [Commands](#commands)
   - [init](#init---create-configuration)
   - [export](#export---export-schema-from-database)
   - [split-dump](#split-dump---split-sql-dump)
   - [diff](#diff---compare-schemas)
   - [watch](#watch---monitor-schema-changes)
   - [migrate](#migrate---generate-migrations)
   - [diagram](#diagram---generate-er-diagram)
   - [drift](#drift)
   - [plan and apply](#plan-and-apply)
   - [fingerprint](#fingerprint)
4. [Common Parameters](#common-parameters)
5. [Object Types](#object-types)
6. [Usage Examples](#usage-examples)
7. [Tips and Best Practices](#tips-and-best-practices)

---

## Overview

**PgSchemaExporter** is a utility for exporting PostgreSQL schemas into a directory structure suitable for Git storage. It enables:

- Exporting schemas from live PostgreSQL databases
- Splitting existing SQL dumps into separate files
- Comparing two schemas and finding differences
- Detecting schema drift against a live database
- Generating migration scripts between schema versions
- Reviewing and applying declarative migration plans
- Validating schema fingerprints
- Monitoring schema changes in real-time
- Generating ER diagrams (Mermaid and Graphviz DOT)
- Profiling command performance

**Version:** 1.9.0

---

## Installation

### Building from Source

```bash
dotnet build src/PgSchemaExporter.Cli/PgSchemaExporter.Cli.csproj -c Release
```

After building, the executable is located at:
- `src/PgSchemaExporter.Cli/bin/Release/net8.0/PgSchemaExporter.Cli.exe` (Windows)
- `src/PgSchemaExporter.Cli/bin/Release/net8.0/PgSchemaExporter.Cli` (Linux/macOS)

### Checking Version

```bash
pgschema-export --version
# or
pgschema-export -v
```

Output:
```
pgschema-export 1.9.0
```

### Getting Help

```bash
pgschema-export --help
# or
pgschema-export -h
```

---

## Commands

### init — Create Configuration

Creates a template configuration file `pgschema-export.json` with default settings.

#### Syntax

```bash
pgschema-export init [--output <path>] [--force]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--output` | `-o` | Path for the configuration file | `pgschema-export.json` |
| `--force` | | Overwrite existing configuration file | false |

#### Examples

**Basic usage:**
```bash
pgschema-export init
```
Creates `pgschema-export.json` in the current directory.

**Specify path:**
```bash
pgschema-export init --output "./config/export-config.json"
```

**Overwrite existing file:**
```bash
pgschema-export init --force
```

#### Configuration Template Content

```json
{
  "ConnectionString": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=secret",
  "OutputDirectory": "./db-schema",
  "Schemas": [
    "public"
  ],
  "ExcludeSchemas": [],
  "CleanOutputDirectory": false,
  "DryRun": false,
  "Parallel": false,
  "Include": {
    "Schemas": true,
    "Extensions": true,
    "Types": true,
    "Sequences": true,
    "Domains": true,
    "ForeignTables": true,
    "Tables": true,
    "Constraints": true,
    "Indexes": true,
    "Views": true,
    "Triggers": true,
    "EventTriggers": true,
    "Rules": true,
    "Aggregates": true,
    "Operators": true,
    "Casts": true,
    "Publications": true,
    "Subscriptions": true,
    "Policies": true,
    "Comments": true,
    "Grants": true,
    "Functions": true
  }
}
```

#### Using the Configuration

After creating and editing the configuration file:

```bash
pgschema-export export --config pgschema-export.json
```

---

### export — Export Schema from Database

Exports a schema from a live PostgreSQL database into a directory structure.

#### Syntax

```bash
pgschema-export export --connection "<connection-string>" --output "<directory>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--connection` | `-c` | PostgreSQL connection string | Required |
| `--output` | `-o` | Export directory | Required |
| `--config` | | Path to JSON configuration file | - |
| `--schemas` | | Comma-separated schema list | `public` |
| `--exclude-schemas` | | Comma-separated schemas to exclude | - |
| `--clean` | | Clean directory before export | false |
| `--dry-run` | | Only show what would be exported | false |
| `--parallel` | | Run metadata queries concurrently | false |
| `--include-<kind>` | | Include object kind in export | true (default) |
| `--exclude-<kind>` | | Exclude object kind from export | - |

#### Connection String Format

Npgsql connection string format:

```
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=secret
```

Or using URI format:

```
postgresql://postgres:secret@localhost:5432/mydb
```

#### Examples

**Basic export:**
```bash
pgschema-export export \
  --connection "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=secret" \
  --output "./db-schema"
```

**Export multiple schemas:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --schemas "public,audit,reports"
```

**Export with schema exclusion:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --exclude-schemas "temp,pg_toast"
```

**Clean directory before export:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --clean
```

**Dry run (no file writing):**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --dry-run
```

Output:
```
Dry run completed. No files were written.
Would export to: /full/path/to/db-schema
Objects discovered: 42
  tables: 15
  views: 8
  functions: 12
  indexes: 7
```

**Parallel export (faster for large databases):**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --parallel
```

**Using configuration file:**
```bash
pgschema-export export --config pgschema-export.json
```

**Export only tables and views:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --exclude-functions \
  --exclude-triggers \
  --exclude-extensions
```

**Export without comments and grants:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --exclude-comments \
  --exclude-grants
```

#### Exported Directory Structure

```
db-schema/
├── schemas/
│   ├── public.sql
│   └── audit.sql
├── extensions/
│   ├── uuid-ossp.sql
│   └── pgcrypto.sql
├── types/
│   └── public.status_enum.sql
├── sequences/
│   └── public.users_id_seq.sql
├── domains/
├── foreign_tables/
├── tables/
│   ├── public.users.sql
│   └── public.orders.sql
├── constraints/
│   └── public.users.constraints.sql
├── indexes/
│   └── public.users.indexes.sql
├── views/
│   └── public.user_summary.sql
├── functions/
│   └── public.calculate_total.<hash>.sql
├── triggers/
│   └── public.update_timestamp.sql
├── event_triggers/
├── rules/
├── publications/
├── subscriptions/
├── policies/
├── comments/
├── grants/
├── casts/
├── aggregates/
├── operators/
├── deploy.sql
└── README.md
```

#### When to Use Parallel Export

Use `--parallel` for:
- Large databases with many objects (100+ tables, 50+ functions)
- Databases with slow metadata queries
- When network latency to the database is high

The parallel mode uses connection pooling and semaphore-based concurrency control to run metadata queries concurrently, significantly reducing export time on large databases.

---

### split-dump — Split SQL Dump

Splits an existing `pg_dump` schema-only SQL file into separate files organized by object type.

#### Syntax

```bash
pgschema-export split-dump --input "<file.sql>" --output "<directory>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--input` | `-i` | Input SQL file from pg_dump | Required |
| `--output` | `-o` | Output directory | Required |
| `--clean` | | Clean output directory before split | false |
| `--no-deploy` | | Do not generate deploy.sql | false |

#### Creating the Input Dump

Recommended `pg_dump` command:

```bash
pg_dump --schema-only --no-owner --no-privileges --file schema.sql mydb
```

#### Examples

**Basic split:**
```bash
pgschema-export split-dump \
  --input "./schema.sql" \
  --output "./db-schema"
```

**Clean output directory first:**
```bash
pgschema-export split-dump \
  -i "./schema.sql" \
  -o "./db-schema" \
  --clean
```

**Skip deploy script generation:**
```bash
pgschema-export split-dump \
  -i "./schema.sql" \
  -o "./db-schema" \
  --no-deploy
```

#### When to Use split-dump

Use `split-dump` when:
- You have an existing `pg_dump` file and want to convert it to the Git-native format
- Migrating from a different schema management tool
- You need to import a schema from a production database that you can't connect to directly

---

### diff — Compare Schemas

Compares two exported schema directories and reports differences. Can also compare against live databases.

#### Syntax

```bash
pgschema-export diff --left "<baseline>" --right "<target>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--left` | `-l` | Baseline (old) exported schema directory | Required* |
| `--left-db` | | Baseline live PostgreSQL connection string | - |
| `--right` | `-r` | Target (new) exported schema directory | Required* |
| `--right-db` | | Target live PostgreSQL connection string | - |
| `--output` | `-o` | Optional path to write diff report | - |
| `--format` | | Output format: text, json, or html | text |
| `--schemas` | | Comma-separated schemas to export for live-db diff | public |
| `--exclude-schemas` | | Comma-separated schemas to exclude for live-db diff | pg_catalog, information_schema |
| `--parallel` | | Run live-db metadata queries concurrently | off |
| `--ignore-comments` | | Ignore SQL comments when comparing files | off |
| `--ignore-whitespace` | | Ignore whitespace-only differences | off |
| `--context` | | Show line-by-line changes within each changed file | off |

*At least one of `--left`/`--left-db` and `--right`/`--right-db` must be specified.

#### Statistics and Context

Every diff report includes a **Changes by type** summary that groups add/remove/change counts by object type (the top-level export folder, e.g. `tables`, `views`, `functions`). Pass `--context` to also emit git-style line-by-line changes for each changed file. In JSON output these appear as the additive `statistics` and `fileDiffs` arrays.

```bash
pgschema-export diff -l "./db-schema-v1" -r "./db-schema-v2" --context --ignore-whitespace
```

#### Format Auto-Detection

When `--format` is not specified, the format is inferred from the `--output` file extension:
- `.json` → JSON format
- `.html` or `.htm` → HTML format
- Other → Text format

#### Exit Codes

- `0` - No differences found
- `2` - Differences found

#### Examples

**Compare two directories (text output):**
```bash
pgschema-export diff \
  --left "./db-schema-v1" \
  --right "./db-schema-v2"
```

Output:
```
Schema Differences:
===================

Added Objects:
  + schemas/public/tables/new_table.sql
  + schemas/public/views/new_view.sql

Removed Objects:
  - schemas/public/tables/old_table.sql

Modified Objects:
  ~ schemas/public/tables/users.sql
    - Column: email (type changed from varchar(100) to varchar(255))
    - Constraint: users_email_key (changed from UNIQUE to UNIQUE CHECK)
```

**Compare with JSON output:**
```bash
pgschema-export diff \
  -l "./db-schema-v1" \
  -r "./db-schema-v2" \
  --format json
```

**Compare with HTML output (auto-detected):**
```bash
pgschema-export diff \
  -l "./db-schema-v1" \
  -r "./db-schema-v2" \
  --output "./diff-report.html"
```

**Compare with explicit HTML format:**
```bash
pgschema-export diff \
  -l "./db-schema-v1" \
  -r "./db-schema-v2" \
  --format html \
  --output "./report.html"
```

**Compare directory against live database:**
```bash
pgschema-export diff \
  --left "./db-schema-v1" \
  --right-db "Host=localhost;Database=mydb;Username=postgres;Password=secret"
```

**Compare two live databases:**
```bash
pgschema-export diff \
  --left-db "Host=localhost;Database=mydb-staging;Username=postgres;Password=secret" \
  --right-db "Host=localhost;Database=mydb-production;Username=postgres;Password=secret"
```

**Save diff to file:**
```bash
pgschema-export diff \
  -l "./db-schema-v1" \
  -r "./db-schema-v2" \
  --output "./diff.txt"
```

#### HTML Report Features

The HTML format provides:
- Styled, self-contained report (no external dependencies)
- Color-coded changes (green for additions, red for removals, yellow for modifications)
- Expandable sections for each changed object
- Easy navigation between object types
- Suitable for sharing with team members or including in CI/CD reports

#### JSON Output Format

```json
{
  "hasDifferences": true,
  "added": [
    "schemas/public/tables/new_table.sql",
    "schemas/public/views/new_view.sql"
  ],
  "removed": [
    "schemas/public/tables/old_table.sql"
  ],
  "modified": [
    {
      "path": "schemas/public/tables/users.sql",
      "changes": [
        "Column: email (type changed from varchar(100) to varchar(255))"
      ]
    }
  ]
}
```

---

### watch — Monitor Schema Changes

Continuously re-runs a directory diff as files change, with debouncing to avoid excessive recomputation.

#### Syntax

```bash
pgschema-export watch --left "<baseline>" --right "<target>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--left` | `-l` | Baseline (old) exported schema directory | Required |
| `--right` | `-r` | Target (new) exported schema directory | Required |
| `--format` | | Console report format: text, json, or html | text |

#### Behavior

- Monitors the `--right` directory for `.sql` file changes
- Uses debouncing (500ms) to avoid recomputing on every keystroke
- Displays diff reports in the console as changes occur
- Press `Ctrl+C` to stop watching
- Cannot compare against live databases (directories only)

#### Examples

**Basic watch:**
```bash
pgschema-export watch \
  --left "./db-schema-v1" \
  --right "./db-schema-v2"
```

Output:
```
Watching for schema changes. Press Ctrl+C to stop.
Left:  /full/path/to/db-schema-v1
Right: /full/path/to/db-schema-v2

[14:32:15] Schema diff:
Schema Differences:
===================
No differences found.

[14:35:22] Schema diff:
Schema Differences:
===================

Modified Objects:
  ~ schemas/public/tables/users.sql
    - Column: email (type changed from varchar(100) to varchar(255))
```

**Watch with JSON output:**
```bash
pgschema-export watch \
  -l "./db-schema-v1" \
  -r "./db-schema-v2" \
  --format json
```

#### When to Use watch

Use `watch` when:
- Developing schema changes and want immediate feedback
- Reviewing pull requests that modify schema files
- Debugging schema migration scripts
- Want to see the impact of changes in real-time

---

### migrate — Generate Migrations

Generates up/down migration scripts between two exported schema directories.

#### Syntax

```bash
pgschema-export migrate --from "<baseline>" --to "<target>" --output "<directory>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--from` | `-f` | Baseline (old) exported schema directory | Required |
| `--to` | `-t` | Target (new) exported schema directory | Required |
| `--output` | `-o` | Output directory for migrations | `./migrations` |
| `--name` | `-n` | Optional name appended to file names | - |
| `--safe` | | Emit destructive statements as comments | false |
| `--preview` | | Print migration to stdout without writing files | false |
| `--online-ddl` | | Rewrite `CREATE`/`DROP INDEX` to `CONCURRENTLY` forms outside the transaction | false |
| `--lock-timeout` | | Emit `SET lock_timeout` guard (e.g. `5s`) | - |
| `--statement-timeout` | | Emit `SET statement_timeout` guard (e.g. `1min`) | - |
| `--warn-hazards` | | Print a hazard analysis of the migration | false |

#### Migration File Naming

Files are named with timestamp: `YYYYMMDDHHMMSS_<name>.sql`

#### Examples

**Basic migration:**
```bash
pgschema-export migrate \
  --from "./db-schema-v1" \
  --to "./db-schema-v2" \
  --output "./migrations"
```

Output:
```
Migration generated.
Up:   /full/path/to/migrations/20240105143000_up.sql
Down: /full/path/to/migrations/20240105143000_down.sql
Statements: 5 up / 5 down
```

**Migration with custom name:**
```bash
pgschema-export migrate \
  -f "./db-schema-v1" \
  -t "./db-schema-v2" \
  -o "./migrations" \
  --name "add_user_email_field"
```

Files: `20240105143000_add_user_email_field_up.sql` and `20240105143000_add_user_email_field_down.sql`

#### Migration History

Every generated migration is recorded in `migrations/history.json` with timestamps, source/target schema directories, applied status, and a destructive flag. This provides an auditable trail of all migrations produced by the tool.

**Safe mode (destructive as comments):**
```bash
pgschema-export migrate \
  -f "./db-schema-v1" \
  -t "./db-schema-v2" \
  -o "./migrations" \
  --safe
```

Output:
```
Migration generated.
Up:   /full/path/to/migrations/20240105143000_up.sql
Down: /full/path/to/migrations/20240105143000_down.sql
Statements: 5 up / 5 down
Note: destructive statements were emitted as comments (--safe). Review before running.
```

**Preview migration (no file writing):**
```bash
pgschema-export migrate \
  -f "./db-schema-v1" \
  -t "./db-schema-v2" \
  --preview
```

Output:
```
-- Up Migration
BEGIN;

ALTER TABLE "public"."users" ALTER COLUMN "email" TYPE varchar(255);

COMMIT;

-- ----------------------------------------------------------------
-- Down Migration
BEGIN;

ALTER TABLE "public"."users" ALTER COLUMN "email" TYPE varchar(100);

COMMIT;
```

#### Destructive Changes

Destructive changes include:
- `DROP TABLE`, `DROP VIEW`, `DROP FUNCTION`, etc.
- Column type changes that may lose data
- Removing constraints
- Removing indexes

In `--safe` mode, these are emitted as comments starting with `-- SAFE:` for review before enabling.

#### Migration Script Structure

**Up migration (applies changes):**
```sql
BEGIN;

-- Add new table
CREATE TABLE "public"."new_table" (
    "id" serial PRIMARY KEY,
    "name" text NOT NULL
);

-- Modify existing table
ALTER TABLE "public"."users" ALTER COLUMN "email" TYPE varchar(255);

-- Add new index
CREATE INDEX "users_email_idx" ON "public"."users" ("email");

COMMIT;
```

**Down migration (reverts changes):**
```sql
BEGIN;

-- Remove new index
DROP INDEX "public"."users_email_idx";

-- Revert table modification
ALTER TABLE "public"."users" ALTER COLUMN "email" TYPE varchar(100);

-- Remove new table
DROP TABLE "public"."new_table";

COMMIT;
```

---

### diagram — Generate ER Diagram

Generates an ER diagram from a live PostgreSQL database or an exported schema directory.

#### Syntax

```bash
pgschema-export diagram --connection "<connection-string>" --output "<file>" [options]
# or
pgschema-export diagram --schema "<directory>" --output "<file>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--connection` | `-c` | PostgreSQL connection string | - |
| `--schema` | `-s` | Exported schema directory | - |
| `--output` | `-o` | Output file path | stdout |
| `--format` | | `mermaid` or `dot` | inferred from output extension |
| `--schemas` | | Comma-separated schemas to include | `public` |
| `--exclude-schemas` | | Comma-separated schemas to exclude | - |

Either `--connection` or `--schema` must be provided.

#### Format Auto-Detection

If `--format` is not specified, it is inferred from the `--output` extension:

- `.mmd` / `.mermaid` → Mermaid
- `.dot` / `.gv` → Graphviz DOT

#### Examples

**Generate Mermaid diagram from live database:**
```bash
pgschema-export diagram \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  --output "schema.mmd"
```

**Generate DOT diagram from exported schema directory:**
```bash
pgschema-export diagram \
  --schema "./db-schema" \
  --output "schema.dot"
```

**Render DOT to SVG:**
```bash
dot -Tsvg schema.dot -o schema.svg
```

**Specify explicit format:**
```bash
pgschema-export diagram \
  --schema "./db-schema" \
  --output "schema.txt" \
  --format mermaid
```

### drift

Detects changes between a committed schema directory and a live database. Reports unexpected, missing, and modified objects.

#### Syntax

```bash
pgschema-export drift --schema "<directory>" --connection "<conn>" [options]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--schema` | `-s` | Exported schema directory | Required |
| `--connection` | `-c` | Live PostgreSQL connection string | Required |
| `--output` | `-o` | File to write the report to | stdout |
| `--format` | | `text` or `json` | text |

#### Exit codes

- `0` — no drift detected
- `2` — drift detected

#### Examples

**Text report:**
```bash
pgschema-export drift \
  --schema "./db-schema" \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=secret"
```

**JSON report for CI:**
```bash
pgschema-export drift \
  --schema "./db-schema" \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  --format json \
  --output drift-report.json
```

---

### plan and apply

A Terraform-style declarative workflow. `plan` produces a reviewable JSON or text plan; `apply` runs it against a live database.

#### plan

##### Syntax

```bash
pgschema-export plan --from "<baseline>" --to "<target>" --output "<file>" [options]
```

##### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--from` | `-f` | Baseline exported schema directory | Required |
| `--to` | `-t` | Target exported schema directory | Required |
| `--output` | `-o` | Plan file path | Required |
| `--format` | | `text` or `json` | text |
| `--online-ddl` | | Rewrite index statements to `CONCURRENTLY` | false |
| `--lock-timeout` | | `SET lock_timeout` guard value | - |
| `--statement-timeout` | | `SET statement_timeout` guard value | - |
| `--safe` | | Skip destructive statements in the plan | false |
| `--warn-hazards` | | Include hazard analysis in the plan | false |

##### Examples

```bash
pgschema-export plan \
  --from "./db-schema" \
  --to "./db-schema-new" \
  --output plan.json \
  --online-ddl \
  --lock-timeout 5s \
  --statement-timeout 1min
```

The plan file contains up/down SQL, render settings, and a hazard analysis.

#### apply

##### Syntax

```bash
pgschema-export apply --plan "<file>" --connection "<conn>" [options]
```

##### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--plan` | `-p` | Path to the plan file produced by `plan` | Required |
| `--connection` | `-c` | Live PostgreSQL connection string | Required |
| `--dry-run` | | Print statements without executing | false |
| `--rollback` | | Apply the down direction | false |
| `--yes` | | Skip confirmation prompt | false |

##### Examples

```bash
# Preview
pgschema-export apply --plan plan.json --connection "<conn>" --dry-run

# Apply
pgschema-export apply --plan plan.json --connection "<conn>"

# Rollback
pgschema-export apply --plan plan.json --connection "<conn>" --rollback --yes
```

Transactional statements run inside a single transaction. `CONCURRENTLY` index statements run outside it.

---

### fingerprint

Generates or verifies a deterministic SHA256 hash of an exported schema directory.

#### Syntax

```bash
pgschema-export fingerprint --schema "<directory>" --output "<file>" [--verify "<file>"]
```

#### Parameters

| Parameter | Short | Description | Default |
|-----------|-------|-------------|---------|
| `--schema` | `-s` | Exported schema directory | Required |
| `--output` | `-o` | Fingerprint file to write | Required* |
| `--verify` | | Fingerprint file to verify against | - |

\* Required when not using `--verify`.

#### Exit codes

- `0` — match / generated successfully
- `2` — mismatch or schema changed

#### Examples

**Generate a fingerprint:**
```bash
pgschema-export fingerprint \
  --schema "./db-schema" \
  --output ./db-schema/schema.fingerprint.json
```

**Verify later:**
```bash
pgschema-export fingerprint \
  --schema "./db-schema" \
  --verify ./db-schema/schema.fingerprint.json
```

The fingerprint normalizes line endings and ignores file ordering, so it is stable across platforms.

---

## Common Parameters

### Include/Exclude Object Kinds

Both `export` and `diff` commands support fine-grained control over which object types to include or exclude.

### Global Flags

| Flag | Description |
|------|-------------|
| `--verbose` | Show more detailed output |
| `--quiet` | Show only errors |
| `--profile` | Print a per-phase timing summary to stderr on completion |

Use `--profile` to identify slow phases in large databases or CI pipelines:

```bash
pgschema-export export \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  --output "./db-schema" \
  --profile
```

#### Syntax

```bash
--include-<kind>    # Include specific object type
--exclude-<kind>    # Exclude specific object type
```

#### Available Object Kinds

| Kind | Description |
|------|-------------|
| `schemas` | Schema definitions |
| `extensions` | PostgreSQL extensions |
| `types` | Custom types (enums, composites, ranges) |
| `sequences` | Sequences |
| `domains` | Domains |
| `foreign-tables` | Foreign tables (FDW) |
| `tables` | Tables |
| `constraints` | Table constraints (primary keys, foreign keys, unique, check) |
| `indexes` | Indexes |
| `views` | Views |
| `triggers` | Triggers |
| `event-triggers` | Event triggers |
| `rules` | Rewrite rules |
| `aggregates` | Aggregate functions |
| `operators` | Operators |
| `casts` | Type casts |
| `publications` | Logical replication publications |
| `subscriptions` | Logical replication subscriptions |
| `policies` | Row-level security policies |
| `comments` | Object comments |
| `grants` | Privilege grants |
| `functions` | Functions and procedures |

#### Examples

**Export only structure (no data-related objects):**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --exclude-comments \
  --exclude-grants \
  --exclude-policies
```

**Export only tables and constraints:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --exclude-views \
  --exclude-functions \
  --exclude-triggers \
  --exclude-indexes \
  --exclude-extensions \
  --exclude-types \
  --exclude-sequences \
  --exclude-domains \
  --exclude-foreign-tables \
  --exclude-aggregates \
  --exclude-operators \
  --exclude-casts \
  --exclude-publications \
  --exclude-subscriptions \
  --exclude-event-triggers \
  --exclude-rules \
  --exclude-comments \
  --exclude-grants
```

**Include only specific types:**
```bash
pgschema-export export \
  -c "Host=localhost;Database=mydb;Username=postgres;Password=secret" \
  -o "./db-schema" \
  --exclude-schemas \
  --exclude-extensions \
  --exclude-sequences \
  --exclude-domains \
  --exclude-foreign-tables \
  --exclude-constraints \
  --exclude-indexes \
  --exclude-views \
  --exclude-triggers \
  --exclude-event-triggers \
  --exclude-rules \
  --exclude-aggregates \
  --exclude-operators \
  --exclude-casts \
  --exclude-publications \
  --exclude-subscriptions \
  --exclude-policies \
  --exclude-comments \
  --exclude-grants \
  --exclude-functions
```

---

## Object Types

### Detailed Descriptions

#### Schemas
Database schemas (namespaces). Example: `public`, `audit`, `reports`.

#### Extensions
PostgreSQL extensions installed in the database. Example: `uuid-ossp`, `pgcrypto`, `postgis`.

#### Types
Custom data types including:
- **Enums**: Custom enumerated types
- **Composites**: Custom row types
- **Ranges**: Range types

#### Sequences
Sequence generators, typically used for auto-incrementing columns.

#### Domains
Custom data types with constraints, based on existing types.

#### Foreign Tables
Tables accessed via Foreign Data Wrappers (FDW).

#### Tables
Standard database tables with columns and data types.

#### Constraints
Table constraints including:
- Primary keys
- Foreign keys
- Unique constraints
- Check constraints

#### Indexes
Indexes for improving query performance.

#### Views
Virtual tables based on queries.

#### Triggers
Functions that execute on specific events (INSERT, UPDATE, DELETE).

#### Event Triggers
Cluster-level triggers that fire on DDL events.

#### Rules
Query rewrite rules (deprecated but still supported).

#### Aggregates
Custom aggregate functions.

#### Operators
Custom operators for data types.

#### Casts
Type conversion casts between data types.

#### Publications
Logical replication publications for publishing changes.

#### Subscriptions
Logical replication subscriptions for consuming changes.

#### Policies
Row-level security policies.

#### Comments
Comment objects attached to database objects.

#### Grants
Privilege grants on objects.

#### Functions
Stored functions and procedures.

---

## Usage Examples

### Complete Workflow Example

**1. Initialize configuration:**
```bash
pgschema-export init --output "./config.json"
```

**2. Edit config.json with your connection details**

**3. Export initial schema:**
```bash
pgschema-export export --config "./config.json" --output "./db-schema-v1"
```

**4. Commit to Git:**
```bash
git add db-schema-v1
git commit -m "Initial schema export"
```

**5. Make database changes**

**6. Export new schema:**
```bash
pgschema-export export --config "./config.json" --output "./db-schema-v2"
```

**7. Compare changes:**
```bash
pgschema-export diff \
  --left "./db-schema-v1" \
  --right "./db-schema-v2" \
  --output "./diff-report.html"
```

**8. Generate migration:**
```bash
pgschema-export migrate \
  --from "./db-schema-v1" \
  --to "./db-schema-v2" \
  --output "./migrations" \
  --name "feature_x_changes"
```

**9. Review and apply migration**

**10. Commit new schema:**
```bash
git add db-schema-v2 migrations
git commit -m "Add feature X schema changes"
```

### CI/CD Integration

**Check for schema drift in CI:**
```bash
# Export current production schema
pgschema-export export \
  -c "$PRODUCTION_DB_CONNECTION" \
  -o "./production-schema" \
  --dry-run

# Compare with committed schema
pgschema-export diff \
  --left "./committed-schema" \
  --right "./production-schema"

# Exit code 2 indicates drift
if [ $? -eq 2 ]; then
  echo "Schema drift detected!"
  exit 1
fi
```

### Development Workflow with Watch

**While developing schema changes:**
```bash
# Terminal 1: Watch for changes
pgschema-export watch \
  --left "./db-schema-base" \
  --right "./db-schema-dev"

# Terminal 2: Edit schema files in db-schema-dev
# Changes will be automatically detected and diffed
```

---

## Tips and Best Practices

### Export Best Practices

1. **Use configuration files** for reproducible exports
2. **Exclude sensitive objects** like grants when committing to public repositories
3. **Use `--dry-run`** first to verify what will be exported
4. **Use `--parallel`** for large databases to speed up exports
5. **Export specific schemas** only when you don't need the entire database

### Schema Organization

1. **Commit schema exports to Git** for version control
2. **Use meaningful branch names** for schema changes
3. **Keep migration files** alongside schema exports
4. **Use consistent directory naming** (e.g., `db-schema-v1`, `db-schema-v2`)

### Migration Best Practices

1. **Always preview migrations** with `--preview` before generating
2. **Use `--safe` mode** initially to review destructive changes
3. **Test down migrations** to ensure rollback is possible
4. **Name migrations descriptively** with `--name` parameter
5. **Keep migrations in version control** alongside schema exports

### Diff Best Practices

1. **Use HTML format** for sharing diffs with team members
2. **Use JSON format** for programmatic consumption in CI/CD
3. **Compare against live databases** for production validation
4. **Use exit codes** in scripts to detect schema drift

### Performance Tips

1. **Enable `--parallel`** for databases with 100+ objects
2. **Use `--exclude-schemas`** to skip system schemas you don't need
3. **Export only needed object types** with `--exclude-<kind>`
4. **Use connection pooling** in your connection string for parallel exports
5. **Use `--profile`** to measure which phases are slow before optimizing

### Security Considerations

1. **Never commit connection strings** to version control
2. **Use environment variables** for sensitive data in CI/CD
3. **Exclude grants** when exporting for public repositories
4. **Review generated migrations** before applying to production

### Troubleshooting

**Connection errors:**
- Verify connection string format
- Check database accessibility
- Ensure user has necessary permissions

**Permission denied on subscriptions:**
- This is expected for non-superusers
- The export will continue, skipping subscriptions
- Use `--exclude-subscriptions` to suppress the warning

**Missing objects in export:**
- Check if object types are excluded
- Verify schema inclusion/exclusion lists
- Ensure user has SELECT privileges on system catalogs

**Slow exports:**
- Enable `--parallel` flag
- Check network latency to database
- Consider exporting fewer object types

---

## Version History

### 1.9.0
- Added `diagram` command for Mermaid and Graphviz DOT ER diagrams
- Added `--profile` global flag for per-phase performance summaries

### 1.8.0
- Added declarative `plan` and `apply` migration workflow
- Added online DDL (`CONCURRENTLY`) rewriting for zero-downtime index changes
- Added hazard warnings and lock/statement timeout configuration

### 1.7.0
- Added `drift` detection and `fingerprint` validation
- Added migration history tracking
- Added GitHub Actions drift workflow

### 1.6.0
- Added `--schemas`/`--exclude-schemas` for customizable live-database diff
- Added `--parallel` support for live-database diff
- Added `--ignore-comments` and `--ignore-whitespace` diff options
- Added `--context` for line-by-line changes within changed files
- Added per-object-type diff summary statistics (text/JSON/HTML)

### 1.5.0
- Added `--verbose` and `--quiet` global flags
- Added progress reporting for export and live-database diff
- Added structured logging (ILogger) across export/diff paths
- Added actionable error messages with suggestions
- Added detailed configuration file validation

### 1.4.1
- Fixed watcher cancellation during the debounce window
- Hardened temp-directory cleanup and subscription null-handling
- Normalized schema names (trim + de-duplicate)

### 1.4.0
- Added `init` command for configuration template generation
- Added `watch` command for real-time schema monitoring
- Added HTML diff report format
- Added parallel export option for faster metadata loading
- Improved CLI help and version output

### 1.3.1
- Fixed critical bugs in PostgreSQL metadata queries
- Replaced non-existent PostgreSQL functions with catalog queries
- Fixed type errors and permission handling

### 1.3.0
- Added support for event triggers, rules, aggregates, operators
- Added support for casts, publications, subscriptions
- Improved object coverage in exports and diffs

---

## Additional Resources

- **[README.md](../README.md)**: Project overview and quick start
- **[Performance Refactoring Report](Performance_Refactoring_Report_Full.md)**: Detailed benchmarks and optimization notes
- **[RELEASE_NOTES_2.0.0.md](../RELEASE_NOTES_2.0.0.md)**: Detailed release notes for current version
- **[PostgreSQL Documentation](https://www.postgresql.org/docs/)**
- **[Npgsql Documentation](https://www.npgsql.org/doc/)**
