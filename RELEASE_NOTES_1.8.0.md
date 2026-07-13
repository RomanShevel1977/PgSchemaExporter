# Release Notes v1.8.0

## Production Features

This release brings production-grade migration capabilities: a declarative
**plan/apply** workflow, **online DDL** for zero-downtime index changes,
**hazard warnings**, and **lock/statement timeout** configuration.

### Declarative plan / apply workflow

A Terraform-style workflow for predictable, reviewable deployments:

```bash
# Generate a reviewable plan (exit code 2 when changes are pending)
pgschema-export plan --from ./db-schema --to ./db-schema-new --output plan.json

# Review as human-readable text or machine-readable JSON
pgschema-export plan --from ./db-schema --to ./db-schema-new --format json

# Apply to a live database (prompts for confirmation)
pgschema-export apply --plan plan.json --connection "<conn>"
```

- The plan JSON captures up/down statements, render settings, and a hazard analysis.
- `apply` runs transactional statements inside a single transaction and concurrent
  statements (e.g. concurrent index builds) outside it.
- `--dry-run` previews statements without executing; `--rollback` applies the down
  direction; `--yes` skips the confirmation prompt.
- In a `--safe` plan, destructive statements are skipped automatically on apply.

### Online DDL (zero-downtime)

```bash
pgschema-export migrate --from ./old --to ./new --output ./migrations --online-ddl
```

- `CREATE INDEX` / `CREATE UNIQUE INDEX` / `DROP INDEX` are rewritten to their
  `CONCURRENTLY` forms.
- Concurrent statements are emitted **outside** the `BEGIN/COMMIT` block, since
  PostgreSQL forbids `CONCURRENTLY` inside a transaction.
- Partial-index `WHERE` clauses and other statement details are preserved.

### Hazard warnings

```bash
pgschema-export migrate --from ./old --to ./new --output ./migrations --warn-hazards
```

Categorizes destructive or lock-heavy operations by severity:

- **TableDrop / ColumnDrop** (High) — irreversible data loss.
- **TypeChange** (High) — table rewrite under ACCESS EXCLUSIVE lock.
- **NotNull** (Medium) — full table scan while locked.
- **IndexBuild** (Medium) — non-concurrent index build blocks writes.
- **ObjectDrop / DataLoss** (Medium) — other destructive statements.

Hazards are also embedded in plans and shown before `apply`.

### Lock and statement timeout configuration

```bash
pgschema-export migrate --from ./old --to ./new --output ./migrations \
  --lock-timeout 5s --statement-timeout 1min
```

Emits session-level `SET lock_timeout` / `SET statement_timeout` guards so a
migration fails fast instead of blocking indefinitely behind other queries. The
same guards are applied by `apply` (as `SET LOCAL` inside the transaction).

### New CLI commands and options

```
plan         Generate a reviewable migration plan (declarative workflow)
apply        Apply a migration plan to a live database

migrate:  --online-ddl, --lock-timeout, --statement-timeout, --warn-hazards
```

### Fixes & hardening

- **Timeout injection guard** — `--lock-timeout` / `--statement-timeout` values are
  now validated (`MigrationTimeout`) before being interpolated into SQL, closing a
  SQL-injection vector and catching malformed values early. Validation runs both at
  option time and, defensively, in `apply` for hand-edited plan files.
- **`drift` clean stdout** — the human-readable drift summary and file-written
  notices now go to **stderr**, so `drift --format json` produces valid JSON on
  stdout for CI consumers (e.g. piping to `jq`).
- **`fingerprint --verify` detail** — on a mismatch, the command now reports exactly
  which files were added, removed, or modified (using the manifest's per-file
  hashes, which were previously unused).

### Tests

- Added unit coverage for the online DDL rewriter, hazard analyzer, migration
  rendering (timeouts + concurrent statement placement), plan generation and
  round-tripping, plan rendering, and CLI parsing for `plan`/`apply`.
- Added coverage for timeout validation (including injection strings) and
  fingerprint file-level comparison.

### Compatibility

- Requires .NET 8.0 and Npgsql 8.0.5.
- Backward compatible with 1.7.x. All new features are opt-in; existing
  `migrate` behavior is unchanged unless the new flags are supplied. The
  `RenderUp(bool)` / `RenderDown(bool)` methods are preserved alongside the new
  `MigrationRenderOptions` overloads.
