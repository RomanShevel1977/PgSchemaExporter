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

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var fileHashes = new List<SchemaFileHash>(files.Count);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file.Full)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            var contentHash = ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

            fileHashes.Add(new SchemaFileHash
            {
                Path = file.Relative,
                Hash = contentHash
            });

            var entry = Encoding.UTF8.GetBytes($"{file.Relative}\n{contentHash}\n");
            aggregate.AppendData(entry);
        }

        var overall = ToHexStringLower(aggregate.GetHashAndReset());

        return new SchemaFingerprintResult
        {
            Fingerprint = overall,
            FileCount = fileHashes.Count,
            Files = fileHashes
        };
    }

    private static string ToHexStringLower(byte[] bytes)
    {
        return string.Create(bytes.Length * 2, bytes, static (chars, state) =>
        {
            ReadOnlySpan<char> lookup = "0123456789abcdef";
            for (var i = 0; i < state.Length; i++)
            {
                var b = state[i];
                chars[i * 2] = lookup[b >> 4];
                chars[i * 2 + 1] = lookup[b & 0xF];
            }
        });
    }

    /// <summary>
    /// Compares a freshly computed fingerprint <paramref name="actual"/> against a
    /// stored manifest, reporting which files were added, removed, or modified.
    /// Returns an empty comparison when the manifest has no per-file hashes.
    /// </summary>
    public static SchemaFingerprintComparison CompareFiles(
        IReadOnlyList<SchemaFileHash>? expectedFiles,
        SchemaFingerprintResult actual)
    {
        if (expectedFiles is null || expectedFiles.Count == 0)
            return new SchemaFingerprintComparison();

        var expected = expectedFiles.ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);
        var current = actual.Files.ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);

        var added = new List<string>();
        var removed = new List<string>();
        var modified = new List<string>();

        foreach (var (path, hash) in current)
        {
            if (!expected.TryGetValue(path, out var expectedHash))
                added.Add(path);
            else if (!string.Equals(expectedHash, hash, StringComparison.OrdinalIgnoreCase))
                modified.Add(path);
        }

        foreach (var path in expected.Keys)
        {
            if (!current.ContainsKey(path))
                removed.Add(path);
        }

        added.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);
        modified.Sort(StringComparer.Ordinal);

        return new SchemaFingerprintComparison
        {
            Added = added,
            Removed = removed,
            Modified = modified
        };
    }
}

/// <summary>File-level differences between a stored fingerprint manifest and a recomputed one.</summary>
public sealed class SchemaFingerprintComparison
{
    /// <summary>Files present now but not in the stored manifest.</summary>
    public IReadOnlyList<string> Added { get; init; } = [];

    /// <summary>Files in the stored manifest but missing now.</summary>
    public IReadOnlyList<string> Removed { get; init; } = [];

    /// <summary>Files whose content hash changed.</summary>
    public IReadOnlyList<string> Modified { get; init; } = [];

    public bool HasDifferences => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
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
