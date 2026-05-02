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

## ✨ Features

### 🧱 Git-Native Schema Export

* Splits schema into atomic files
* Logical directory structure
* Human-readable SQL
* Perfect for Git versioning

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

* .NET 8 / 9
* C#
* Npgsql
* Cross-platform CLI

---

## 👨‍💻 Author

Built by a developer who was tired of:

* unreadable dumps
* broken migrations
* and debugging SQL in 50k-line files

---

## ⭐ Support

If this project helps you:

* ⭐ Star the repo
* 🐛 Report issues
* 💡 Suggest features

---

## 📜 License

MIT (or your chosen license)

---

## 🔥 One-line Pitch

> "Make PostgreSQL behave like a real codebase."
