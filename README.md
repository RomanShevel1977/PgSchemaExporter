![GitHub release](https://img.shields.io/github/v/release/RomanShevel1977/PgSchemaExporter)
![License](https://img.shields.io/github/license/RomanShevel1977/PgSchemaExporter)
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

## Why not just pg_dump?

`pg_dump` is great for backups.

PgSchemaExporter is for development workflows:

* Git-friendly structure
* readable schema
* code review
* CI/CD

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

---

## Example use cases

* review DB changes in pull requests
* version schema in Git
* generate clean migrations
* refactor legacy databases

---

## Release Notes

See [RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md) for the latest changes.

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
