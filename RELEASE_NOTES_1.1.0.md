# Version 1.1.0 Release Notes

## New: Migration generation
- Added a `migrate` command that compares two exported schema directories
  (baseline `--from` and target `--to`) and generates runnable `up`/`down`
  migration scripts.
- **Semantic table diffing:** tables are compared at the column level, emitting
  targeted `ALTER TABLE` statements (`ADD COLUMN`, `DROP COLUMN`,
  `ALTER COLUMN ... TYPE`, `SET/DROP NOT NULL`, `SET/DROP DEFAULT`) instead of
  dropping and recreating the table, so existing data is preserved.
- **Object-aware strategies for other kinds:**
  - Views and functions use `CREATE OR REPLACE` on change.
  - Indexes, constraints, triggers, policies, and grants use statement-level set
    diffing with matching drop/recreate rollback.
  - Types, sequences, domains, schemas, extensions, and foreign tables get
    create/drop handling with correct rollback.
- **Dependency-aware ordering:** up migrations are ordered for safe deployment
  (schemas → extensions → types → ... → grants); down migrations use the reverse.
- Every generated script is wrapped in `BEGIN; ... COMMIT;`.

## Safety
- `--safe` emits destructive statements (DROP, column type changes) as commented
  SQL so they must be reviewed and enabled manually.
- The CLI warns when a generated migration contains destructive statements.
- Tables containing constructs the parser does not understand fall back to a safe
  (explicitly flagged) drop/recreate strategy.

## CLI
- `migrate --from <dir> --to <dir> [--output <dir>] [--name <name>] [--safe] [--preview]`
- `--preview` prints the migration to stdout without writing files.
- Generated files are timestamped: `yyyyMMddHHmmss[_name].up.sql` / `.down.sql`.

## Tests
- Added `TableDefinitionParserTests` and `MigrationGeneratorTests` covering added/
  removed/changed tables, column type/nullability/default changes, view replacement,
  index create/drop, constraint set diffing, and the unparseable-table fallback.
