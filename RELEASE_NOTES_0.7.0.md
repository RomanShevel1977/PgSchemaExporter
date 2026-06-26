# Version 0.7.0 Release Notes

## New
- Added live export support for PostgreSQL triggers.
- Added live export support for PostgreSQL row-level policies.
- Generated trigger files are written to `triggers/`.
- Generated policy files are written to `policies/`.

## Behavior
- Triggers are now exported with `pg_get_triggerdef(...)` and ordered after their table definitions.
- Policies are now exported with `pg_get_policydef(...)` when available.
- Added fallback policy generation for PostgreSQL versions where `pg_get_policydef` does not exist.
- Policy role lists now ignore invalid role OID `0` and correctly serialize `PUBLIC` when no roles are defined.
- Deployment plan now includes dependencies from triggers and policies to their underlying tables and referenced functions.

## Files changed
- `PgSchemaExporter.Core/Models/DbTrigger.cs`
- `PgSchemaExporter.Core/Models/DbPolicy.cs`
- `PgSchemaExporter.Core/Models/DatabaseModel.cs`
- `PgSchemaExporter.Core/Options/ExportOptions.cs`
- `PgSchemaExporter.Core/Metadata/PostgresMetadataProvider.cs`
- `PgSchemaExporter.Core/Output/SchemaFileWriter.cs`
- `PgSchemaExporter.Core/Output/DeploymentPlanBuilder.cs`
