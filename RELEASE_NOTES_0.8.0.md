# Version 0.8.0 Release Notes

## New
- Added live export support for PostgreSQL comments.
- Added live export support for PostgreSQL grants.
- Comments are now emitted into `comments/`.
- Grants are now emitted into `grants/`.

## Behavior
- Comment files are generated for schema, table, view, sequence, type, function, column, and constraint comments.
- Grant files are generated for object privileges on schemas, tables, sequences, types, and functions.
- Deployment plan automatically includes comment and grant files and orders them after their referenced objects.

## Files changed
- `PgSchemaExporter.Core/Models/DbComment.cs`
- `PgSchemaExporter.Core/Models/DbGrant.cs`
- `PgSchemaExporter.Core/Models/DatabaseModel.cs`
- `PgSchemaExporter.Core/Options/ExportOptions.cs`
- `PgSchemaExporter.Core/Metadata/PostgresMetadataProvider.cs`
- `PgSchemaExporter.Core/Output/SchemaFileWriter.cs`

## Notes
- Existing policy fallback support remains compatible and is unchanged.
