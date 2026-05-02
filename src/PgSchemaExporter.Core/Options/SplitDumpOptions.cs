namespace PgSchemaExporter.Core.Options;

public sealed class SplitDumpOptions
{
    public string InputFile { get; set; } = "";
    public string OutputDirectory { get; set; } = "./db-schema";
    public bool CleanOutputDirectory { get; set; }
    public bool GenerateDeployScript { get; set; } = true;
}
