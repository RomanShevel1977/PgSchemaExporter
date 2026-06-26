# Version 0.6.1 Release Notes

## Fixes

### 🐛 Fixed constraint deployment order in foreign key scenarios
**Issue:** When exporting PostgreSQL schemas with foreign key constraints, the deployment script would fail with the error:
```
ERROR: there is no unique constraint matching given keys for referenced table
```

**Root Cause:** The `DeploymentPlanBuilder` was creating dependencies between constraint files and their source tables, but not accounting for dependencies between constraint files themselves. When table A had a foreign key referencing table B, the constraint file for A was executed before B's constraints were created, causing the PRIMARY KEY on B to not yet exist.

**Solution:** Enhanced the dependency resolution logic in `DeploymentPlanBuilder.cs` to properly establish dependencies between constraint files. When a constraint references another table (e.g., foreign key), the constraint file now depends on the referenced table's constraint file, ensuring PRIMARY KEYs and UNIQUE constraints are created before foreign keys that reference them.

**Impact:** 
- Schema exports with complex foreign key relationships now deploy successfully
- Correct topological ordering ensures all constraints are created in proper dependency order
- Eliminates deployment failures without requiring manual script reordering

## Technical Details

- **Modified:** `PgSchemaExporter.Core/Output/DeploymentPlanBuilder.cs`
- **Change Type:** Dependency Resolution Enhancement
- **Backward Compatibility:** ✅ Yes

## Testing

Verified with a multi-table schema including:
- Primary keys
- Unique constraints  
- Foreign key constraints with circular dependencies
- All constraints deployed successfully without errors

---

## What's New in 0.6.1

This patch release focuses on stability improvements for complex schema deployments.
