![GitHub release](https://img.shields.io/github/v/release/RomanShevel1977/PgSchemaExporter)
[![License](https://img.shields.io/badge/license-MIT-green)](./LICENSE)
![Stars](https://img.shields.io/github/stars/RomanShevel1977/PgSchemaExporter)
# PostgreSQL Git-Native Schema Exporter

> Make PostgreSQL behave like a real codebase.

Git-native PostgreSQL schema exporter and `pg_dump` splitter.

---

## Why

`pg_dump` produces one giant SQL file — 50k+ lines, huge diffs for tiny changes,
impossible to review. PgSchemaExporter writes **one file per database object** in a
structured folder tree, so schema changes look like normal code changes:

```text
db-schema/
├── tables/public.users.sql
├── indexes/
├── constraints/
├── views/
├── functions/
├── types/
├── sequences/
├── triggers/
├── policies/
└── deploy.sql          # dependency-ordered deployment script
```

---

## Features

- **Export** — dump a live database to structured per-object files; parallel, with configurable object inclusion.
- **Split** — convert an existing monolithic `pg_dump` into the same Git-friendly layout.
- **Diff** — compare directory↔directory, directory↔live, or live↔live; text/JSON/HTML output, per-type stats, context-aware line diffs, ignore comments/whitespace.
- **Migrate** — generate `up`/`down` scripts with data-preserving semantic `ALTER`s; safe and preview modes; history tracking.
- **Plan / Apply** — Terraform-style reviewable plan, then apply (or roll back) against a live database.
- **Production-safe** — online DDL (`CONCURRENTLY`), lock/statement timeouts, hazard analysis of destructive operations.
- **Drift & Fingerprint** — detect out-of-band changes vs the committed schema; deterministic SHA256 validation.
- **ER Diagrams** — generate Mermaid `erDiagram` or Graphviz DOT from a live DB or exported schema.
- **Watch** — auto-re-diff/export as the schema changes.
- **CI/CD** — machine-readable JSON, meaningful exit codes, ready-to-use GitHub Action.
- **DX** — `init` config scaffolding + validation, structured logging, `--verbose`/`--quiet`, progress reporting, `--profile` timing summary.

**Supported objects:** tables (+ partitions), views (+ materialized), functions/procedures/aggregates,
indexes, constraints, triggers, event triggers, rules, row-level policies, types
(composite/range/enum/domain), sequences, foreign tables, operators, casts,
publications/subscriptions, extensions.

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

See the full guide in [USAGE_GUIDE.md](doc/USAGE_GUIDE.md). Run `pgschema-export --help`
for all flags. Commands at a glance:

| Command | Purpose |
| --- | --- |
| `export` | Export a live database to structured files |
| `split-dump` | Split an existing `pg_dump` into the same layout |
| `diff` | Compare two schemas (dir/live, any combination) |
| `migrate` | Generate `up`/`down` migration scripts |
| `plan` / `apply` | Reviewable plan, then apply/roll back on a live DB |
| `drift` | Check a live DB against the committed schema |
| `fingerprint` | Generate/verify a deterministic schema hash |
| `diagram` | Generate an ER diagram (Mermaid or Graphviz DOT) |
| `watch` | Re-diff/export automatically on changes |
| `init` | Scaffold a config file |

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

The diff report is written to **stdout** (so `drift --format json | jq` stays
valid), while the human-readable summary goes to **stderr**.

> **Note:** drift compares your committed directory against a full live export.
> For accurate results, commit a schema directory produced by `export` with the
> default (complete) object coverage. A partial export can surface objects that
> exist in the database as false "unexpected" drift.

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

### Generate an ER diagram

Visualize the schema as a Mermaid `erDiagram` (renders in GitHub/GitLab Markdown)
or as Graphviz DOT for publication-quality SVG/PNG:

```bash
# From a live database
pgschema-export diagram \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --output schema.mmd

# From an exported schema directory
pgschema-export diagram \
  --schema "./db-schema" \
  --output schema.dot
```

Useful flags:

```bash
pgschema-export diagram --schema "./db-schema" --format mermaid
pgschema-export diagram --connection "<conn>" --schemas "public" --output schema.gv
```

The renderer uses primary keys, unique constraints, and foreign keys to show
key markers and relationship cardinality. The `dot` output can be rendered with:

```bash
dot -Tsvg schema.dot -o schema.svg
```

---

### Profile performance

Add `--profile` to any command to print a per-phase timing summary to stderr
when the operation completes:

```bash
pgschema-export export --connection "<conn>" --output ./db-schema --profile
```

This is useful for finding slow phases in large databases or CI pipelines.

---

### Production-safe migrations

`migrate` supports zero-downtime and safety options:

```bash
pgschema-export migrate --from ./old --to ./new --output ./migrations \
  --online-ddl \                # CREATE/DROP INDEX rewritten to CONCURRENTLY (outside txn)
  --lock-timeout 5s \           # emit SET lock_timeout guard
  --statement-timeout 1min \    # emit SET statement_timeout guard
  --warn-hazards                # print a hazard analysis of the migration
```

- **`--online-ddl`** rewrites index builds to `CONCURRENTLY` and emits them
  outside the transaction to avoid blocking writes.
- **`--lock-timeout` / `--statement-timeout`** add session guards so a migration
  fails fast instead of blocking behind a long-running query.
- **`--warn-hazards`** categorizes destructive/lock-heavy operations
  (`TableDrop`, `ColumnDrop`, `TypeChange`, `NotNull`, `IndexBuild`, …) by
  severity so you can review them before running.

---

### Declarative plan / apply workflow

A Terraform-style workflow: generate a reviewable plan, then apply it.

```bash
# 1. Generate a plan (human-readable by default; exit code 2 if changes are pending)
pgschema-export plan --from ./db-schema --to ./db-schema-new --output plan.json

# 2. Review the plan (JSON is machine-readable, e.g. for PR automation)
pgschema-export plan --from ./db-schema --to ./db-schema-new --format json

# 3. Apply the plan to a live database (prompts for confirmation)
pgschema-export apply --plan plan.json --connection "Host=localhost;Database=mydb;Username=postgres;Password=123"

# Preview without executing, or roll back:
pgschema-export apply --plan plan.json --dry-run
pgschema-export apply --plan plan.json --connection "<conn>" --rollback --yes
```

The plan captures the up/down statements, render settings (online DDL, timeouts),
and a hazard analysis. On apply, transactional statements run inside a single
transaction; concurrent statements (e.g. concurrent index builds) run outside it.
In a `--safe` plan, destructive statements are skipped automatically.

---

## Exit codes (CI/CD)

- **0** — success / no differences
- **1** — error (bad arguments, missing files, connection failure)
- **2** — differences or drift detected

```bash
pgschema-export diff --left ./db-schema --right ./db-schema-live --format json
if [ $? -eq 2 ]; then echo "Schema changes detected"; exit 1; fi
```

Built for teams that version their database in Git: review DB changes in pull
requests, generate clean migrations, and gate CI on schema drift. Currently
schema-only (no data migration).

---

## Roadmap

Shipped through **v1.9.0**: export, `pg_dump` split, schema diff, dependency-ordered
deploy, migration generation, live-to-live diff, broad object coverage, watch mode,
HTML reports, parallel export, structured logging, advanced diff options, drift
detection, schema fingerprints, declarative plan/apply workflow with online DDL and
hazard warnings, ER-diagram visualization (Mermaid and Graphviz DOT), and
`--profile` performance summaries.

**Next:**
- **v2.0.0** — multi-database support (MySQL, SQLite, SQL Server), cloud integrations, AI-assisted migrations.

<details>
<summary>Full release history</summary>

* v0.6.0  Deployment Manifest
* v0.7.0  Triggers and Policies Export
* v0.8.0  Schema Diff
* v0.9.0  Dependency Graph
* v1.0.0  Stability, diagnostics, and broader PostgreSQL coverage
* v1.1.0  Migration Generation — semantic diff and runnable `ALTER` up/down scripts
* v1.2.0  Live-to-Live Diff & CI/CD — live database comparison, GitHub Action, JSON diff output
* v1.3.0  Broader Object Coverage — event triggers, rules, aggregates, operators, casts, publications/subscriptions, composite/range types
* v1.3.1  Bug fixes — catalog-based DDL generation for the new object kinds
* v1.4.0  Developer Experience — watch mode, `init` command, HTML diff report, parallel export
* v1.4.1  Bug fixes — watcher cancellation, temp-dir cleanup, subscription null-handling, schema normalization
* v1.5.0  Developer Experience Enhancements — structured logging, progress reporting, `--verbose`/`--quiet`, actionable errors, config validation
* v1.6.0  Advanced Diff Features — customizable/parallel live-db diff, `--ignore-comments`/`--ignore-whitespace`, context-aware line diffs, per-type statistics
* v1.7.0  Safety & CI/CD — drift detection, schema fingerprint validation, migration history tracking, GitHub Action drift workflow
* v1.8.0  Production Features — declarative plan/apply workflow, online DDL (concurrent indexes), hazard warnings, lock/statement timeout configuration
* v1.9.0  Developer Experience — ER-diagram visualization (Mermaid/Graphviz DOT), performance profiling, `--profile` flag

</details>

Latest changes: [RELEASE_NOTES_1.9.0.md](RELEASE_NOTES_1.9.0.md).

---

## License

MIT. Contributions and issues welcome — if this project helps you, ⭐ the repo.
