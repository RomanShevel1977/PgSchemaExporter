# Release Notes v1.3.1

## Bug Fixes — Broader Object Coverage

This patch release fixes critical bugs in the v1.3.0 object coverage. The v1.3.0 metadata queries relied on several PostgreSQL functions that do not exist, which caused the export to fail whenever the new object kinds were enabled. All DDL is now built directly from the system catalogs.

### Fixed: non-existent PostgreSQL functions

The following queries used functions that are not part of PostgreSQL and threw errors at runtime. They now construct DDL from the system catalogs:

- **Event triggers** — replaced `pg_get_event_triggerdef` with a query over `pg_event_trigger` + `pg_proc`.
- **Operators** — replaced `pg_get_operatordef` with a query over `pg_operator`.
- **Casts** — replaced `pg_get_castdef` with a query over `pg_cast` (cast method/context).
- **Publications** — replaced `pg_get_publicationdef` with a query over `pg_publication`.
- **Subscriptions** — replaced `pg_get_subscriptiondef` with a query over `pg_subscription`.
- **Composite / range types** — replaced `pg_get_typedef` with queries over `pg_attribute` / `pg_range`.
- **Aggregates** — replaced `pg_get_functiondef` (which errors on aggregates) with a hand-built `CREATE AGGREGATE` from `pg_aggregate`.

### Fixed: type and logic errors

- **Event trigger tags** — `evttags` (a `text[]`) was read as a string and threw. Now serialized via `array_to_string` / `unnest`.
- **Publications** — `array_to_string(puballtables, ',')` was applied to a boolean column. Publication tables now come from `pg_publication_tables`, with the `publish` list derived from `pubinsert/pubupdate/pubdelete/pubtruncate`.
- **Composite types** — every table implicitly defines a composite type. A `relkind = 'c'` filter was added so only standalone composite types are exported (previously a bogus composite type was emitted for every table).
- **Subscriptions** — `pg_subscription` is cluster-wide. Results are now filtered to the current database via `current_database()`.
- **Casts** — previously enumerated all non-implicit built-in casts (hundreds). Now filtered to casts involving the requested schemas.
- **Unary operators** — `oprleft`/`oprright` of `0` produced invalid `-` types. These are now treated as absent (`LEFTARG`/`RIGHTARG` omitted).
- **Rules** — the automatic `_RETURN` view rule is now excluded to avoid duplicating view definitions.

### Fixed: robustness

- **Subscription permissions** — `pg_subscription.subconninfo` is restricted to superusers. Reading it as a non-superuser raised `permission denied` and aborted the entire export (subscriptions are enabled by default). This is now handled gracefully (SQLSTATE `42501`), degrading to an empty subscription list instead of failing.

### Fixed: consistency

- **Live diff** — `SchemaDiffer` now explicitly enables the v1.3.0 object kinds when exporting live databases for comparison.
- **Dry-run report** — the export summary now counts event triggers, rules, aggregates, operators, casts, publications, and subscriptions.

### Tests

- Added `TypeScriptGeneratorTests` covering enum, composite, range, and unsupported type kinds.

### Compatibility

- Requires .NET 8.0 and Npgsql 8.0.5.
- Fully backward compatible with 1.3.0; no CLI or output-layout changes.
