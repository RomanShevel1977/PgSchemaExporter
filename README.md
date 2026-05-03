# 🐘 PostgreSQL Git-Native Schema Exporter

> Turn your PostgreSQL database into clean, structured, Git-friendly code.

Stop fighting with massive `pg_dump` files.
Start managing your database like a real software project.

---

## 🚀 Overview

**PostgreSQL Git-Native Schema Exporter** is a CLI tool that transforms:

* a live PostgreSQL database
* or an existing `pg_dump` file

into a structured, readable, version-controllable SQL project.

Instead of a single unreadable SQL dump, you get:

```
db-schema/
├── schemas/
├── tables/
├── indexes/
├── constraints/
├── views/
├── functions/
├── types/
├── sequences/
└── deploy.sql
```

---

## ❌ The Problem

`pg_dump` is powerful — but painful:

* One huge SQL file (10k–100k+ lines)
* Impossible to review in Git
* Hard to debug changes
* No logical structure
* Not CI/CD friendly

---

## ✅ The Solution

This tool converts your database into:

* 📁 **Structured folders**
* 📄 **One file per object**
* 🔍 **Clean Git diffs**
* ⚙️ **Deploy-ready scripts**

---

## 🧠 Why not just pg_dump?

`pg_dump` is great for backups.

PgSchemaExporter is focused on development workflows:
- readable schema structure
- clean Git diffs
- code review
- CI/CD-friendly output

---

## 🚧 Project Status

This project is in early public release (`v0.5.0`).

It is usable for schema export and pg_dump splitting, but APIs and output format may still change before `v1.0.0`.

---

## ✨ Features

### 🧱 Git-Native Schema Export

* Splits schema into atomic files
* Logical directory structure
* Human-readable SQL
* Perfect for Git versioning

---

## Before vs After

### ❌ pg_dump
- 50,000 lines
- impossible to review
- no structure

### ✅ PgSchemaExporter
- one file per table
- clean diffs
- readable SQL

---


### 🔄 Split Existing pg_dump

Already have a dump?

```bash
pgschema-export split-dump --input schema.sql --output ./db-schema
```

No database connection required.

---

### 🧠 Safe SQL Parsing

Handles complex PostgreSQL syntax:

* `$$ function bodies $$`
* strings and escaped quotes
* comments (`--`, `/* */`)
* multi-line SQL blocks

---

### ⚡ CLI First

* Lightweight
* Scriptable
* CI/CD ready
* Cross-platform (.NET)

---

## 📥 Install

Download the latest binary from the [Releases](../../releases) page.

### Windows

```powershell
pgschema-export.exe --help

### Linux / macOS

chmod +x pgschema-export
./pgschema-export --help

---

## 🛠 Usage

### Export from PostgreSQL

```bash
pgschema-export export \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --output "./db-schema" \
  --schemas public \
  --clean
```

---

### Split pg_dump

```bash
pg_dump --schema-only --no-owner --no-privileges --file schema.sql mydb

pgschema-export split-dump \
  --input "./schema.sql" \
  --output "./db-schema" \
  --clean
```

---

## 📦 Output Structure

```
db-schema/
├── tables/
│   └── public.users.sql
├── indexes/
│   └── public.users.indexes.sql
├── constraints/
│   └── public.users.constraints.sql
├── views/
│   └── public.active_users.sql
├── functions/
│   └── public.normalize_email.sql
├── types/
├── sequences/
└── deploy.sql
```

---

## 💡 Use Cases

* Git-based schema versioning
* Code review for database changes
* CI/CD pipelines
* Safe environment replication
* Refactoring legacy databases
* Dev ↔ Prod synchronization

---

## 🧪 Recommended pg_dump Options

```bash
pg_dump \
  --schema-only \
  --no-owner \
  --no-privileges \
  --file schema.sql \
  mydb
```

---

## ⚠️ Limitations (MVP)

Currently optimized for schema-only dumps.

Not fully supported yet:

* `COPY` / data migration
* `GRANT` / permissions
* ownership metadata
* complex extensions

---

## 🔮 Roadmap

* Dependency-aware ordering
* Data migration engine (COPY)
* Smart anonymization (GDPR-safe)
* VS Code extension
* CI/CD integrations
* Schema diff engine

---

## 🧠 Philosophy

> Your database schema is code. Treat it like code.

This tool brings:

* clarity
* structure
* maintainability

to PostgreSQL workflows.

---

## 🏗 Tech Stack

- .NET 8
- C#
- Npgsql

---

## 🙌 Feedback

This is an early release, so feedback is very welcome.

If you use PostgreSQL and have ever struggled with reviewing schema dumps in Git, please open an issue with your use case or suggestions.