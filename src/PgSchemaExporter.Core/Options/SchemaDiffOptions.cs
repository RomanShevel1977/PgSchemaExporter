namespace PgSchemaExporter.Core.Options;

public sealed class SchemaDiffOptions
{
    public string LeftDirectory { get; set; } = "";
    public string RightDirectory { get; set; } = "";
    public string? OutputFile { get; set; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(LeftDirectory))
            throw new ArgumentException("Left directory is required (--left).");

        if (string.IsNullOrWhiteSpace(RightDirectory))
            throw new ArgumentException("Right directory is required (--right).");

        if (!Directory.Exists(LeftDirectory))
            throw new DirectoryNotFoundException($"Left directory was not found: {LeftDirectory}");

        if (!Directory.Exists(RightDirectory))
            throw new DirectoryNotFoundException($"Right directory was not found: {RightDirectory}");
    }
}
