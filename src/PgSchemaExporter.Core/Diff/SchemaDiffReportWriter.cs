using System.Text;
using System.Text.Json;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Diff;

public sealed class SchemaDiffReportWriter
{
    public string BuildReport(SchemaDiffResult result, DiffFormat format = DiffFormat.Text)
    {
        return format switch
        {
            DiffFormat.Json => BuildJsonReport(result),
            _ => BuildTextReport(result)
        };
    }

    public async Task WriteAsync(string outputFile, SchemaDiffResult result, DiffFormat format = DiffFormat.Text, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFile));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputFile, BuildReport(result, format), cancellationToken);
    }

    private static string BuildTextReport(SchemaDiffResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Schema diff report");
        sb.AppendLine();
        sb.AppendLine($"- Added:     {result.Added.Count}");
        sb.AppendLine($"- Removed:   {result.Removed.Count}");
        sb.AppendLine($"- Changed:   {result.Changed.Count}");
        sb.AppendLine($"- Unchanged: {result.Unchanged.Count}");
        sb.AppendLine();

        AppendSection(sb, "Added", result.Added, "+");
        AppendSection(sb, "Removed", result.Removed, "-");
        AppendSection(sb, "Changed", result.Changed, "~");

        if (!result.HasDifferences)
            sb.AppendLine("No differences detected.");

        return sb.ToString();
    }

    private static string BuildJsonReport(SchemaDiffResult result)
    {
        var json = new
        {
            added = result.Added,
            removed = result.Removed,
            changed = result.Changed,
            unchanged = result.Unchanged,
            hasDifferences = result.HasDifferences
        };

        return JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<string> items, string marker)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine($"## {title} ({items.Count})");
        foreach (var item in items)
            sb.AppendLine($"{marker} {item}");
        sb.AppendLine();
    }
}
