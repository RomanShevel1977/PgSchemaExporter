using System.Security.Cryptography;
using System.Text;

namespace PgSchemaExporter.Core.Integrity;

/// <summary>
/// Computes a deterministic SHA256 fingerprint of an exported schema directory.
/// The fingerprint covers every <c>.sql</c> file's relative path and normalized
/// content, so it can be used to detect drift or validate that a migration is
/// being applied against the expected schema state.
/// </summary>
public static class SchemaFingerprint
{
    /// <summary>
    /// Computes the fingerprint for the schema directory at <paramref name="directory"/>.
    /// Line endings are normalized so the fingerprint is stable across platforms.
    /// </summary>
    public static SchemaFingerprintResult Compute(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory was not found: {directory}");

        var files = Directory
            .EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Select(f => new
            {
                Relative = Path.GetRelativePath(directory, f).Replace('\\', '/'),
                Full = f
            })
            .OrderBy(x => x.Relative, StringComparer.Ordinal)
            .ToList();

        using var sha = SHA256.Create();
        var fileHashes = new List<SchemaFileHash>(files.Count);

        using var aggregate = new MemoryStream();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file.Full)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            var contentHash = Convert.ToHexString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

            fileHashes.Add(new SchemaFileHash
            {
                Path = file.Relative,
                Hash = contentHash
            });

            var entry = Encoding.UTF8.GetBytes($"{file.Relative}\n{contentHash}\n");
            aggregate.Write(entry, 0, entry.Length);
        }

        aggregate.Position = 0;
        var overall = Convert.ToHexString(sha.ComputeHash(aggregate)).ToLowerInvariant();

        return new SchemaFingerprintResult
        {
            Fingerprint = overall,
            FileCount = fileHashes.Count,
            Files = fileHashes
        };
    }
}

public sealed class SchemaFingerprintResult
{
    /// <summary>The overall SHA256 fingerprint of the schema directory.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Number of <c>.sql</c> files that contributed to the fingerprint.</summary>
    public int FileCount { get; init; }

    /// <summary>Per-file hashes, ordered by relative path.</summary>
    public IReadOnlyList<SchemaFileHash> Files { get; init; } = [];
}

public sealed class SchemaFileHash
{
    public string Path { get; init; } = "";
    public string Hash { get; init; } = "";
}
