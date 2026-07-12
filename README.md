![GitHub release](https://img.shields.io/github/v/release/RomanShevel1977/PgSchemaExporter)
[![License](https://img.shields.io/badge/license-MIT-green)](./LICENSE)
![Stars](https://img.shields.io/github/stars/RomanShevel1977/PgSchemaExporter)
# PostgreSQL Git-Native Schema Exporter

> Make PostgreSQL behave like a real codebase.

Git-native PostgreSQL schema exporter and `pg_dump` splitter.

---

## Why this exists

If you've ever tried to use `pg_dump` with Git, you know the pain:

### ❌ pg_dump

* 50k+ lines SQL files
* impossible to review changes
* huge diffs for tiny updates
* no structure

---

## This tool fixes that

### ✅ PgSchemaExporter

* one file per object
* clean Git diffs
* readable SQL
* structured folders

---

## What you get

Instead of this:

```text
schema.sql (50,000 lines)
```

You get:

```text
db-schema/
├── tables/
│   └── public.users.sql
├── indexes/
├── constraints/
├── views/
├── functions/
├── types/
├── sequences/
├── domains/
├── foreign_tables/
├── triggers/
├── policies/
└── deploy.sql
```

Now database changes look like normal code changes

---

## Features

### Core Capabilities

**Export from PostgreSQL**
- Export schema from live database to structured files
- One file per database object (tables, views, functions, etc.)
- Support for all common PostgreSQL objects
- Parallel export for faster performance on large databases
- Configurable object inclusion (domains, foreign tables, etc.)

**Split Existing pg_dump**
- Split monolithic pg_dump files into structured format
- Convert existing schema dumps to Git-friendly structure
- Preserve all schema information

**Schema Diff**
- Compare two schema directories
- Compare directory to live database
- Compare two live databases
- Multiple output formats (text, JSON, HTML)
- Per-type statistics (tables, views, functions, etc.)
- Context-aware line diffs (see exact line changes)
- Ignore comments and whitespace options
- Parallel live database comparison

**Migration Generation**
- Generate up/down migration scripts between schema versions
- Semantic diff (ALTER statements instead of drop/recreate)
- Data preservation
- Safe mode (comment out destructive SQL)
- Preview mode (print without writing)

**Watch Mode**
- Monitor live database for schema changes
- Auto-export on change detection
- Real-time schema synchronization

**Init Command**
- Initialize configuration file
- Quick setup for new projects
- Config validation

**HTML Diff Report**
- Visual diff reports for stakeholders
- Highlighted additions, removals, and context
- Easy to share in pull requests

### Advanced Features

**Parallel Export**
- Concurrent metadata queries
- Faster export on large databases
- Configurable parallelism

**Live-to-Live Diff**
- Compare two live databases directly
- No intermediate export required
- Useful for staging vs production comparison

**Customizable Schema Selection**
- Include/exclude specific schemas
- Fine-grained control over comparison scope
- Useful for multi-schema databases

**Advanced Diff Options**
- Ignore SQL comments (whole-line and trailing)
- Ignore whitespace differences
- Context-aware line diffs
- Per-type change statistics

**Structured Logging**
- JSON logging for integration
- Configurable log levels (verbose/quiet)
- Actionable error messages

**Progress Reporting**
- Real-time progress updates
- Time estimates
- Cancellation support

**Config Validation**
- Validate configuration files
- Clear error messages
- Schema validation

### CI/CD Integration

**Exit Codes**
- 0: Success (no differences)
- 1: Error
- 2: Differences detected

**JSON Output**
- Machine-readable diff output
- Easy to parse in CI/CD pipelines
- Structured change information

**GitHub Action Ready**
- Easy integration with GitHub Actions
- Schema drift detection
- Pull request comments

### Supported Objects

- Tables (including partitions)
- Views (including materialized views)
- Functions (including procedures and aggregates)
- Indexes
- Constraints (primary keys, foreign keys, unique, check)
- Triggers
- Policies (row-level security)
- Types (composite, range, enum, domains)
- Sequences
- Foreign tables
- Event triggers
- Rules
- Operators
- Casts
- Publications and subscriptions
- Extensions

---

## Install

Download the latest binary:

https://github.com/RomanShevel1977/PgSchemaExporter/releases

### Linux / macOS

```bash
chmod +x pgschema-export
./pgschema-export --help
```

### Windows

```powershell
pgschema-export.exe --help
```

---

## Usage

See the full guide [USAGE_GUIDE.md](USAGE_GUIDE.md)

### Export from PostgreSQL

```bash
pgschema-export export \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --output "./db-schema"
```

Useful flags:

```bash
pgschema-export export \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --output "./db-schema" \
  --include-domains \
  --include-foreign-tables
```

---

### Split existing pg_dump

```bash
pg_dump --schema-only --no-owner --no-privileges --file schema.sql mydb

pgschema-export split-dump \
  --input "./schema.sql" \
  --output "./db-schema"
```

---

### Generate a migration between two exports

Compare a baseline export against a target export and generate runnable
`up`/`down` migration scripts:

```bash
pgschema-export migrate \
  --from "./db-schema-old" \
  --to "./db-schema-new" \
  --output "./migrations" \
  --name "add_age_column"
```

Tables are diffed semantically, so column changes become targeted `ALTER TABLE`
statements (instead of drop/recreate) and your data is preserved:

```sql
-- Up migration (apply changes)
BEGIN;
ALTER TABLE "public"."users" ADD COLUMN "age" integer DEFAULT 0;
ALTER TABLE "public"."users" ALTER COLUMN "email" SET NOT NULL;
COMMIT;
```

Useful flags:

```bash
pgschema-export migrate --from ./old --to ./new --preview   # print, don't write
pgschema-export migrate --from ./old --to ./new --safe      # comment out destructive SQL
```

Every generated migration is recorded in `migrations/history.json` for an
auditable trail of what was produced, when, and whether it was destructive.

---

### Detect schema drift

Verify that a live database still matches the schema committed to Git. Drift is
anything present in the live DB but not in the committed schema (or vice versa):

```bash
pgschema-export drift \
  --schema "./db-schema" \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --format json \
  --output drift-report.json
```

Exit code `2` signals drift was detected, which makes it easy to fail a CI job.
See [.github/workflows/schema-drift.yml](.github/workflows/schema-drift.yml) for
a ready-to-use scheduled drift check.

---

### Fingerprint a schema

Generate a deterministic SHA256 fingerprint of an exported schema directory. Use
it to validate that a schema has not changed unexpectedly:

```bash
# Generate and store a fingerprint
pgschema-export fingerprint --schema "./db-schema" --output ./db-schema/schema.fingerprint.json

# Later, verify the schema still matches (exit code 2 on mismatch)
pgschema-export fingerprint --schema "./db-schema" --verify ./db-schema/schema.fingerprint.json
```

The fingerprint normalizes line endings, so it is stable across platforms and
independent of file ordering.

---

## Why not just pg_dump?

`pg_dump` is great for backups.

PgSchemaExporter is for development workflows:

* Git-friendly structure
* readable schema
* code review
* CI/CD

---

## Exit codes for CI/CD

The following exit codes are used for CI gating:

- **0** — Success (no differences detected, or operation completed successfully)
- **1** — Error (invalid arguments, missing files, connection failure, etc.)
- **2** — Differences detected (used by `diff` command to signal schema changes)

Example CI check:

```bash
pgschema-export diff --left ./db-schema --right ./db-schema-live --format json
if [ $? -eq 2 ]; then
  echo "Schema changes detected!"
  exit 1
fi
```

---

## Designed for

* Backend developers
* DevOps engineers
* Teams using Git for DB versioning

---

## Current status (v1.0.0 direction)

* schema-only focus
* no data migration yet
* broader PostgreSQL coverage, including domains, foreign tables, and materialized views
* improved deployment ordering for real-world object dependencies

---

## Roadmap

* v0.6.0  Deployment Manifest ✅
* v0.7.0  Triggers and Policies Export ✅
* v0.8.0  Schema Diff ✅
* v0.9.0  Dependency Graph ✅
* v1.0.0  Stability, diagnostics, and broader PostgreSQL coverage ✅
* v1.1.0  Migration Generation — semantic diff and runnable `ALTER` up/down scripts ✅
* v1.2.0  Live-to-Live Diff & CI/CD — live database comparison, GitHub Action, JSON diff output ✅
* v1.3.0  Broader Object Coverage — event triggers, rules, aggregates, operators, casts, publications/subscriptions, composite/range types ✅
* v1.3.1  Bug fixes — catalog-based DDL generation for the new object kinds ✅
* v1.4.0  Developer Experience — watch mode, `init` command, HTML diff report, parallel export ✅
* v1.4.1  Bug fixes — watcher cancellation, temp-dir cleanup, subscription null-handling, schema normalization ✅
* v1.5.0  Developer Experience Enhancements — structured logging, progress reporting, `--verbose`/`--quiet`, actionable errors, config validation ✅
* v1.6.0  Advanced Diff Features — customizable/parallel live-db diff, `--ignore-comments`/`--ignore-whitespace`, context-aware line diffs, per-type statistics ✅
* v1.7.0  Safety & CI/CD — drift detection, schema fingerprint validation, migration history tracking, GitHub Action drift workflow ✅

What will be done in the next releases:

### v1.8.0 - Production Features (Planned)
**Features:**
- Declarative plan mode (Terraform-style plan-review-apply workflow)
- Online DDL support (zero-downtime migrations, concurrent indexes)
- Hazard warnings (destructive operation detection)
- Lock timeout configuration
- Schema documentation generation

### v1.9.0 - Developer Experience (Planned)
**Features:**
- Schema visualization (ER diagrams)
- Performance profiling
- Improved error messages
- Better progress reporting

### v2.0.0 - Strategic Expansion (Planned)
**Features:**
- Multi-database support (MySQL, SQLite, SQL Server)
- Cloud integration (AWS RDS, GCP Cloud SQL, Azure)
- AI-assisted migration generation

---

## Example use cases

* review DB changes in pull requests
* version schema in Git
* generate clean migrations
* refactor legacy databases

---

## Release Notes

See [RELEASE_NOTES_1.7.0.md](RELEASE_NOTES_1.7.0.md) for the latest changes.

## Feedback

If you've ever struggled with `pg_dump` in Git — this tool is for you.

Open an issue or share your workflow.

---

## Support

If this project helps you:

* ⭐ Star the repo
* 🐛 Report issues
* 💡 Suggest features

---

## License

MIT
