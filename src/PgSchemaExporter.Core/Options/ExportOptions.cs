namespace PgSchemaExporter.Core.Options;

public sealed class ExportOptions
{
    public string ConnectionString { get; set; } = "";
    public string OutputDirectory { get; set; } = "./db-schema";
    public string[] Schemas { get; set; } = ["public"];
    public string[] ExcludeSchemas { get; set; } = ["pg_catalog", "information_schema"];
    public bool CleanOutputDirectory { get; set; }
    public bool DryRun { get; set; }
    public bool Parallel { get; set; }
    public IncludeOptions Include { get; set; } = new();
    public FormatOptions Format { get; set; } = new();

    public void EnsureValidForExport()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));

        // Normalize schemas: trim whitespace and remove duplicates
        Schemas = Schemas
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Collects every validation problem instead of throwing on the first one, so
    /// callers can report them all at once with actionable guidance.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ConnectionString))
            errors.Add("Connection string is required. Set 'connectionString' (or pass --connection).");

        if (string.IsNullOrWhiteSpace(OutputDirectory))
            errors.Add("Output directory is required. Set 'outputDirectory' (or pass --output).");

        if (Schemas is null || Schemas.Length == 0 || Schemas.All(string.IsNullOrWhiteSpace))
            errors.Add("At least one schema is required. Set 'schemas' to a non-empty list (e.g. [\"public\"]).");
        else if (Schemas.Any(string.IsNullOrWhiteSpace))
            errors.Add("The 'schemas' list contains empty entries. Remove blank values.");

        if (Include is null)
            errors.Add("The 'include' section is missing or null.");

        if (Format is null)
            errors.Add("The 'format' section is missing or null.");

        return errors;
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
    public bool EventTriggers { get; set; } = true;
    public bool Rules { get; set; } = true;
    public bool Aggregates { get; set; } = true;
    public bool Operators { get; set; } = true;
    public bool Casts { get; set; } = true;
    public bool Publications { get; set; } = true;
    public bool Subscriptions { get; set; } = true;
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
