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

---

## Example use cases

* review DB changes in pull requests
* version schema in Git
* generate clean migrations
* refactor legacy databases

---

## Release Notes

See [RELEASE_NOTES_1.4.0.md](RELEASE_NOTES_1.4.0.md) for the latest changes.

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
