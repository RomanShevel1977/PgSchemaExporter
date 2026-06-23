using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Configuration;

public static class ExportConfigLoader
{
    public static async Task<ExportOptions> LoadAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Config file was not found.", configPath);

        var json = await File.ReadAllTextAsync(configPath, cancellationToken);

        var options = JsonSerializer.Deserialize<ExportOptions>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

        return options ?? throw new InvalidOperationException("Config file is empty or invalid.");
    }
}
