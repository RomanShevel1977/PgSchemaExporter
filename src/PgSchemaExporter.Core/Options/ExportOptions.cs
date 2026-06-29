namespace PgSchemaExporter.Core.Options;

public sealed class ExportOptions
{
    public string ConnectionString { get; set; } = "";
    public string OutputDirectory { get; set; } = "./db-schema";
    public string[] Schemas { get; set; } = ["public"];
    public string[] ExcludeSchemas { get; set; } = ["pg_catalog", "information_schema"];
    public bool CleanOutputDirectory { get; set; }
    public bool DryRun { get; set; }
    public IncludeOptions Include { get; set; } = new();
    public FormatOptions Format { get; set; } = new();

    public void EnsureValidForExport()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("Connection string is required.");

        if (string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("Output directory is required.");

        if (Schemas is null || Schemas.Length == 0 || Schemas.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one schema is required.");

        Schemas = Schemas
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class IncludeOptions
{
    public bool Schemas { get; set; } = true;
    public bool Extensions { get; set; } = true;
    public bool Types { get; set; } = true;
    public bool Sequences { get; set; } = true;
    public bool Domains { get; set; } = true;
    public bool ForeignTables { get; set; } = true;
    public bool Tables { get; set; } = true;
    public bool Constraints { get; set; } = true;
    public bool Indexes { get; set; } = true;
    public bool Views { get; set; } = true;
    public bool Triggers { get; set; } = true;
    public bool Policies { get; set; } = true;
    public bool Comments { get; set; } = true;
    public bool Grants { get; set; } = true;
    public bool Functions { get; set; } = true;
}

public sealed class FormatOptions
{
    public bool UseIfNotExists { get; set; } = true;
    public bool SplitConstraints { get; set; } = true;
    public bool SplitIndexes { get; set; } = true;
}
