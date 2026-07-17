using System.Text.Json;

namespace PgSchemaExporter.Core.Migration.Plan;

/// <summary>Reads and writes <see cref="MigrationPlan"/> JSON files.</summary>
public static class MigrationPlanFile
{
    public static string Serialize(MigrationPlan plan)
        => JsonSerializer.Serialize(plan, Serialization.PgSchemaExporterJsonContext.Default.MigrationPlan);

    public static async Task WriteAsync(string path, MigrationPlan plan, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, Serialize(plan), cancellationToken);
    }

    public static async Task<MigrationPlan> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Plan file was not found: {path}", path);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize(json, Serialization.PgSchemaExporterJsonContext.Default.MigrationPlan)
            ?? throw new InvalidOperationException($"Plan file is empty or invalid: {path}");
    }
}
