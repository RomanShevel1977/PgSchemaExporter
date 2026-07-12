# Release Notes v1.7.0

## Safety & CI/CD

This release adds production-safety and CI/CD features focused on catching
unintended schema changes: **drift detection**, **schema fingerprinting**, and
**migration history tracking**, plus a ready-to-use GitHub Action drift workflow.

### Drift detection

The new `drift` command compares a committed schema directory (the expected
state) against a live PostgreSQL database (the actual state) and reports any
deviation:

```bash
pgschema-export drift \
  --schema ./db-schema \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --format json \
  --output drift-report.json
```

- Objects present in the live DB but not committed are reported as **unexpected**.
- Objects committed but missing from the live DB are reported as **missing**.
- Objects that exist in both but differ are reported as **modified**.
- Exit code `2` signals drift was detected (ideal for failing a CI job).
- Supports the same comparison controls as `diff`: `--schemas`,
  `--exclude-schemas`, `--parallel`, `--ignore-comments`, `--ignore-whitespace`,
  and `--context`.

### Schema fingerprint validation

The new `fingerprint` command computes a deterministic SHA256 fingerprint of an
exported schema directory, which can be stored and later verified:

```bash
# Generate and store a fingerprint
pgschema-export fingerprint --schema ./db-schema --output ./db-schema/schema.fingerprint.json

# Verify the schema still matches (exit code 2 on mismatch)
pgschema-export fingerprint --schema ./db-schema --verify ./db-schema/schema.fingerprint.json
```

- Fingerprints normalize line endings, so they are stable across platforms.
- The fingerprint is independent of file creation order.
- The manifest (`schema.fingerprint.json`) records the overall fingerprint,
  file count, generation timestamp, and per-file hashes.

### Migration history tracking

The `migrate` command now appends a record to `migrations/history.json` for
every generated migration, providing an auditable trail:

```json
{
  "migrations": [
    {
      "appliedAt": "2026-07-12T16:00:00+00:00",
      "name": "add_age_column",
      "upFile": "20260712160000_add_age_column.up.sql",
      "downFile": "20260712160000_add_age_column.down.sql",
      "upStatements": 2,
      "downStatements": 2,
      "destructive": false
    }
  ]
}
```

History is only written for real runs; `--preview` does not record an entry.

### GitHub Action drift workflow

A new `.github/workflows/schema-drift.yml` runs a scheduled (and on-demand)
drift check against a live database, verifies the committed fingerprint, uploads
a drift report artifact, and fails the job when drift is detected.

### New CLI commands

```
drift        Detect drift between a committed schema directory and a live DB
fingerprint  Compute (or verify) a SHA256 fingerprint of a schema directory
```

### Tests

- Added unit coverage for `SchemaFingerprint` (stability, line-ending and
  order independence, round-tripping the manifest), `MigrationHistory`
  (append/read, ordering, destructive flag), and CLI parsing for the new
  `drift` and `fingerprint` commands.

### Compatibility

- Requires .NET 8.0 and Npgsql 8.0.5.
- Backward compatible with 1.6.x. All new features are additive; existing
  commands, configs, and report consumers work unchanged.
