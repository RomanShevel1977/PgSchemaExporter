using System.Text.Json;
using System.Text.Json.Serialization;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Integrity;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Plan;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Serialization;

/// <summary>
/// Source-generated JSON serialization context for plan, history, and fingerprint
/// files. Eliminates reflection-based metadata resolution at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExportOptions))]
[JsonSerializable(typeof(IncludeOptions))]
[JsonSerializable(typeof(FormatOptions))]
[JsonSerializable(typeof(MigrationPlan))]
[JsonSerializable(typeof(MigrationPlanSettings))]
[JsonSerializable(typeof(PlanStatement))]
[JsonSerializable(typeof(PlanHazard))]
[JsonSerializable(typeof(MigrationHistoryFile))]
[JsonSerializable(typeof(MigrationHistoryEntry))]
[JsonSerializable(typeof(SchemaFingerprintManifest))]
[JsonSerializable(typeof(SchemaFingerprintResult))]
[JsonSerializable(typeof(SchemaFileHash))]
[JsonSerializable(typeof(SchemaDiffReportDto))]
[JsonSerializable(typeof(SchemaDiffStatDto))]
[JsonSerializable(typeof(SchemaFileDiffDto))]
[JsonSerializable(typeof(SchemaDiffLineDto))]
public partial class PgSchemaExporterJsonContext : JsonSerializerContext
{
}
