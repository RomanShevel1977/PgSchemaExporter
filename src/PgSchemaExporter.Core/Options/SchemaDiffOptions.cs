namespace PgSchemaExporter.Core.Options;

public sealed class SchemaDiffOptions
{
    public string LeftDirectory { get; set; } = "";
    public string LeftConnectionString { get; set; } = "";
    public string RightDirectory { get; set; } = "";
    public string RightConnectionString { get; set; } = "";
    public string? OutputFile { get; set; }
    public DiffFormat Format { get; set; } = DiffFormat.Text;

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
