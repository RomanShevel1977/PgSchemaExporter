namespace PgSchemaExporter.Core.Diagramming;

/// <summary>
/// Options for generating an ER diagram. Exactly one source must be supplied:
/// a live database (<see cref="ConnectionString"/>) or an exported schema
/// directory (<see cref="SchemaDirectory"/>).
/// </summary>
public sealed class DiagramOptions
{
    public string? ConnectionString { get; set; }
    public string? SchemaDirectory { get; set; }

    public DiagramFormat Format { get; set; } = DiagramFormat.Mermaid;

    /// <summary>Optional path to write the diagram; when null, the diagram is returned to the caller.</summary>
    public string? OutputFile { get; set; }

    /// <summary>Schemas to include when reading a live database.</summary>
    public string[] Schemas { get; set; } = ["public"];

    /// <summary>Schemas to exclude when reading a live database.</summary>
    public string[] ExcludeSchemas { get; set; } = ["pg_catalog", "information_schema"];

    public bool UsesLiveDatabase => !string.IsNullOrWhiteSpace(ConnectionString);

    public void EnsureValid()
    {
        var hasConnection = !string.IsNullOrWhiteSpace(ConnectionString);
        var hasDirectory = !string.IsNullOrWhiteSpace(SchemaDirectory);

        if (hasConnection == hasDirectory)
            throw new ArgumentException("Provide exactly one source: --connection <string> or --schema <dir>.");

        if (hasDirectory && !Directory.Exists(SchemaDirectory))
            throw new DirectoryNotFoundException($"Schema directory was not found: {SchemaDirectory}");
    }
}
