using System.Text.Json;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Serialization;

namespace PgSchemaExporter.Core.Configuration;

public static class ExportConfigLoader
{
    public static async Task<ExportOptions> LoadAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException(
                $"Config file was not found: {configPath}. Run 'pgschema-export init' to create one.",
                configPath);

        var json = await File.ReadAllTextAsync(configPath, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            throw new ConfigValidationException(configPath,
                ["The file is empty. Run 'pgschema-export init --force' to regenerate a template."]);

        ExportOptions? options;
        try
        {
            var serializerOptions = new JsonSerializerOptions(PgSchemaExporterJsonContext.Default.Options)
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                TypeInfoResolver = PgSchemaExporterJsonContext.Default
            };

            options = JsonSerializer.Deserialize<ExportOptions>(json, serializerOptions);
        }
        catch (JsonException ex)
        {
            var location = ex.LineNumber is not null
                ? $" (line {ex.LineNumber + 1}, position {ex.BytePositionInLine + 1})"
                : string.Empty;

            throw new ConfigValidationException(configPath,
                [$"Invalid JSON{location}: {ex.Message}"]);
        }

        if (options is null)
            throw new ConfigValidationException(configPath,
                ["The file did not contain a JSON object."]);

        var errors = options.Validate();
        if (errors.Count > 0)
            throw new ConfigValidationException(configPath, errors);

        return options;
    }
}
