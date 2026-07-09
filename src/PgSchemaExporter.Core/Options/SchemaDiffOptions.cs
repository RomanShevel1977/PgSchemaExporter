namespace PgSchemaExporter.Core.Options;

public sealed class SchemaDiffOptions
{
    public string LeftDirectory { get; set; } = "";
    public string LeftConnectionString { get; set; } = "";
    public string RightDirectory { get; set; } = "";
    public string RightConnectionString { get; set; } = "";
    public string? OutputFile { get; set; }
    public DiffFormat Format { get; set; } = DiffFormat.Text;

    /// <summary>Schemas to include when exporting live databases for comparison.</summary>
    public string[] Schemas { get; set; } = ["public"];

    /// <summary>Schemas to exclude when exporting live databases for comparison.</summary>
    public string[] ExcludeSchemas { get; set; } = ["pg_catalog", "information_schema"];

    /// <summary>Run live-database metadata queries concurrently (faster on large databases).</summary>
    public bool Parallel { get; set; }

    /// <summary>Ignore SQL comments (whole-line and trailing <c>--</c> comments) when comparing files.</summary>
    public bool IgnoreComments { get; set; }

    /// <summary>Ignore whitespace-only differences (leading/trailing/collapsed whitespace, blank lines).</summary>
    public bool IgnoreWhitespace { get; set; }

    /// <summary>Emit line-by-line changes within each changed file (context-aware diff).</summary>
    public bool ShowContext { get; set; }

    public void EnsureValid()
    {
        var hasLeftDir = !string.IsNullOrWhiteSpace(LeftDirectory);
        var hasLeftDb = !string.IsNullOrWhiteSpace(LeftConnectionString);
        var hasRightDir = !string.IsNullOrWhiteSpace(RightDirectory);
        var hasRightDb = !string.IsNullOrWhiteSpace(RightConnectionString);

        if (!hasLeftDir && !hasLeftDb)
            throw new ArgumentException("Either --left or --left-db is required.");

        if (!hasRightDir && !hasRightDb)
            throw new ArgumentException("Either --right or --right-db is required.");

        if (hasLeftDir && !Directory.Exists(LeftDirectory))
            throw new DirectoryNotFoundException($"Left directory was not found: {LeftDirectory}");

        if (hasRightDir && !Directory.Exists(RightDirectory))
            throw new DirectoryNotFoundException($"Right directory was not found: {RightDirectory}");
    }
}

public enum DiffFormat
{
    Text,
    Json,
    Html
}
