# PostgreSQL Git-Native Schema Exporter

CLI tool that exports a PostgreSQL schema into a structured Git-friendly SQL project.

## Features

- Schemas
- Extensions
- Enum types
- Sequences
- Tables
- Constraints
- Indexes
- Views
- Functions
- `deploy.sql`

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run --project src/PgSchemaExporter.Cli -- export \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --output "./db-schema" \
  --schemas public \
  --clean
```

## Run with config

```bash
dotnet run --project src/PgSchemaExporter.Cli -- export --config pgschema-export.example.json
```

## Output

```text
db-schema/
  README.md
  deploy.sql
  extensions/
  schemas/
  types/
  sequences/
  tables/
  constraints/
  indexes/
  views/
  functions/
```

## Publish single file

Windows:

```bash
dotnet publish src/PgSchemaExporter.Cli -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish/win-x64
```

Linux:

```bash
dotnet publish src/PgSchemaExporter.Cli -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true -o publish/linux-x64
```


## Split existing pg_dump file

Recommended dump format:

```bash
pg_dump --schema-only --no-owner --no-privileges --file schema.sql mydb
```

Split it:

```bash
dotnet run --project src/PgSchemaExporter.Cli -- split-dump \
  --input "./schema.sql" \
  --output "./db-schema" \
  --clean
```

Supported in MVP:

- `CREATE SCHEMA`
- `CREATE EXTENSION`
- `CREATE TYPE`
- `CREATE SEQUENCE`
- `CREATE TABLE`
- `ALTER TABLE ... ADD CONSTRAINT`
- `CREATE INDEX`
- `CREATE VIEW`
- `CREATE FUNCTION`

The splitter handles semicolons inside strings, comments and dollar-quoted function bodies.
