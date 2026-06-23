using System.Text.Json;

namespace PgSchemaExporter.Core.Output;

public sealed class DependencyManifestWriter
{
    public async Task WriteAsync(
        string outputDirectory,
        DeploymentPlan deploymentPlan,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(outputDirectory, "dependencies.json");

        await using var stream = File.Create(path);

        await using var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = true
            });

        writer.WriteStartObject();

        writer.WriteString("generatedBy", "PostgreSQL Git-Native Schema Exporter");

        writer.WritePropertyName("orderedFiles");
        writer.WriteStartArray();

        foreach (var file in deploymentPlan.OrderedFiles)
            writer.WriteStringValue(file);

        writer.WriteEndArray();

        writer.WritePropertyName("dependencies");
        writer.WriteStartObject();

        foreach (var item in deploymentPlan.Dependencies.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(item.Key);
            writer.WriteStartArray();

            foreach (var dependency in item.Value.OrderBy(x => x, StringComparer.Ordinal))
                writer.WriteStringValue(dependency);

            writer.WriteEndArray();
        }

        writer.WriteEndObject();

        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken);
    }
}
