using System.Text.Json;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Serialization;

namespace PgSchemaExporter.Core.Configuration;

/// <summary>
/// Writes a template <c>pgschema-export.json</c> configuration file that can be
/// consumed by <see cref="ExportConfigLoader"/> via the <c>--config</c> option.
/// </summary>
public static class ExportConfigWriter
{
    public const string DefaultFileName = "pgschema-export.json";

    public static string BuildTemplate()
    {
        var template = new ExportOptions
        {
            ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres",
            OutputDirectory = "./db-schema",
            Schemas = ["public"],
            ExcludeSchemas = ["pg_catalog", "information_schema"],
            CleanOutputDirectory = false,
            Parallel = false,
            Include = new IncludeOptions(),
            Format = new FormatOptions()
        };

        return JsonSerializer.Serialize(template, PgSchemaExporterJsonContext.Default.ExportOptions);
    }

    /// <summary>
    /// Writes the template to <paramref name="path"/>. When <paramref name="overwrite"/>
    /// is false and the file already exists, an <see cref="IOException"/> is thrown.
    /// </summary>
    public static async Task WriteAsync(string path, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Config path is required.", nameof(path));

        if (File.Exists(path) && !overwrite)
            throw new IOException($"Config file already exists: {path}. Use --force to overwrite.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, BuildTemplate(), cancellationToken);
    }
}
