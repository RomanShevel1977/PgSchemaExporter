using System.Text.Json;

namespace PgSchemaExporter.Core.Integrity;

/// <summary>
/// Reads and writes the on-disk fingerprint manifest (<c>schema.fingerprint.json</c>)
/// used to validate schema state before migrations and in CI/CD pipelines.
/// </summary>
public static class SchemaFingerprintFile
{
    public const string DefaultFileName = "schema.fingerprint.json";

    public static async Task WriteAsync(
        string path,
        SchemaFingerprintResult result,
        bool includeFiles = true,
        CancellationToken cancellationToken = default)
    {
        var manifest = new SchemaFingerprintManifest
        {
            Fingerprint = result.Fingerprint,
            FileCount = result.FileCount,
            GeneratedAt = DateTimeOffset.UtcNow,
            Files = includeFiles ? result.Files : null
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(manifest, Serialization.PgSchemaExporterJsonContext.Default.SchemaFingerprintManifest);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public static async Task<SchemaFingerprintManifest> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fingerprint file was not found: {path}", path);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var manifest = JsonSerializer.Deserialize(json, Serialization.PgSchemaExporterJsonContext.Default.SchemaFingerprintManifest)
            ?? throw new InvalidOperationException($"Fingerprint file is empty or invalid: {path}");

        if (string.IsNullOrWhiteSpace(manifest.Fingerprint))
            throw new InvalidOperationException($"Fingerprint file is missing the fingerprint value: {path}");

        return manifest;
    }
}

public sealed class SchemaFingerprintManifest
{
    public string Fingerprint { get; init; } = "";
    public int FileCount { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<SchemaFileHash>? Files { get; init; }
}
