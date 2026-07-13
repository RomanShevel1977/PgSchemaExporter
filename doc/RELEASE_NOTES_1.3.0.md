# Release Notes v1.3.0

## Broader Object Coverage

This release significantly expands the range of PostgreSQL objects that can be exported, including advanced types, event triggers, rules, aggregates, operators, casts, and logical replication objects.

### New Object Types

- **Event Triggers** — DDL-level event triggers (table `pg_event_trigger`)
- **Rules** — Query rewrite rules for tables and views (table `pg_rewrite`)
- **Aggregates** — Custom aggregate functions (table `pg_aggregate`)
- **Operators** — Custom operators (table `pg_operator`)
- **Casts** — Type casts (table `pg_cast`)
- **Publications** — Logical replication publications (table `pg_publication`)
- **Subscriptions** — Logical replication subscriptions (table `pg_subscription`)
- **Composite Types** — Struct/record types (`typtype = 'c'`)
- **Range Types** — Range types (`typtype = 'r'`)

### CLI Changes

#### New include options:
- `--include-event-triggers` / `--exclude-event-triggers`
- `--include-rules` / `--exclude-rules`
- `--include-aggregates` / `--exclude-aggregates`
- `--include-operators` / `--exclude-operators`
- `--include-casts` / `--exclude-casts`
- `--include-publications` / `--exclude-publications`
- `--include-subscriptions` / `--exclude-subscriptions`

#### Example:
```bash
pgschema-export export \
  --connection "Host=localhost;Database=mydb" \
  --output ./db-schema \
  --include-event-triggers \
  --include-rules \
  --include-aggregates
```

### Internal Changes

- Added model classes: `DbEventTrigger`, `DbRule`, `DbAggregate`, `DbOperator`, `DbCast`, `DbPublication`, `DbSubscription`
- Extended `DbType` model with `CompositeDefinition` and `RangeDefinition` properties
- Added new lists to `DatabaseModel` for all new object types
- Added include flags to `IncludeOptions` for all new object kinds
- Implemented 7 new metadata provider methods in `PostgresMetadataProvider`
- Created 7 new script generators for SQL generation
- Updated `TypeScriptGenerator` to handle composite and range types
- Updated `SchemaFileWriter` with folder mappings for new objects
- Updated `FileWriteResult` with new file lists and deployment order
- Updated CLI help text with new object kinds

### Folder Structure

New folders added to the exported schema:
- `event_triggers/` — Event trigger definitions
- `rules/` — Query rewrite rules
- `aggregates/` — Aggregate function definitions
- `operators/` — Operator definitions
- `casts/` — Type cast definitions
- `publications/` — Publication definitions
- `subscriptions/` — Subscription definitions

### Compatibility

- Requires .NET 8.0
- Requires Npgsql 8.0.5
- Backward compatible with existing export functionality
- New object types are included by default (use `--exclude-<kind>` to skip)
