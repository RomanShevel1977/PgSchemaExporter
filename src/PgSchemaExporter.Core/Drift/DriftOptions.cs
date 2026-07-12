using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Drift;

/// <summary>
/// Options for detecting schema drift between an exported schema directory (the
/// expected/committed state) and a live PostgreSQL database (the actual state).
/// </summary>
public sealed class DriftOptions
{
    /// <summary>The exported schema directory representing the expected state.</summary>
    public string SchemaDirectory { get; set; } = "";

    /// <summary>Connection string of the live database representing the actual state.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Optional path to write the drift report.</summary>
    public string? OutputFile { get; set; }

    public DiffFormat Format { get; set; } = DiffFormat.Text;

    /// <summary>Schemas to include when exporting the live database.</summary>
    public string[] Schemas { get; set; } = ["public"];

    /// <summary>Schemas to exclude when exporting the live database.</summary>
    public string[] ExcludeSchemas { get; set; } = ["pg_catalog", "information_schema"];

    /// <summary>Run live-database metadata queries concurrently.</summary>
    public bool Parallel { get; set; }

    /// <summary>Ignore SQL comments when comparing.</summary>
    public bool IgnoreComments { get; set; }

    /// <summary>Ignore whitespace-only differences when comparing.</summary>
    public bool IgnoreWhitespace { get; set; }

    /// <summary>Emit line-by-line changes within each drifted file.</summary>
    public bool ShowContext { get; set; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(SchemaDirectory))
            throw new ArgumentException("Schema directory is required (--schema).");

        if (!Directory.Exists(SchemaDirectory))
            throw new DirectoryNotFoundException($"Schema directory was not found: {SchemaDirectory}");

        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("Live database connection string is required (--connection).");
    }

    /// <summary>
    /// Maps drift options onto <see cref="SchemaDiffOptions"/>, treating the schema
    /// directory as the baseline (left) and the live database as the target (right).
    /// </summary>
    public SchemaDiffOptions ToDiffOptions()
    {
        return new SchemaDiffOptions
        {
            LeftDirectory = SchemaDirectory,
            RightConnectionString = ConnectionString,
            Format = Format,
            Schemas = Schemas,
            ExcludeSchemas = ExcludeSchemas,
            Parallel = Parallel,
            IgnoreComments = IgnoreComments,
            IgnoreWhitespace = IgnoreWhitespace,
            ShowContext = ShowContext
        };
    }
}
