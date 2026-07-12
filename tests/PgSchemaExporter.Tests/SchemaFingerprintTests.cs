using PgSchemaExporter.Core.Integrity;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaFingerprintTests : IDisposable
{
    private readonly string _root;

    public SchemaFingerprintTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-fp-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Compute_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            SchemaFingerprint.Compute(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void Compute_EmptyDirectory_ProducesStableFingerprint()
    {
        var result = SchemaFingerprint.Compute(_root);

        Assert.Equal(0, result.FileCount);
        Assert.False(string.IsNullOrWhiteSpace(result.Fingerprint));
    }

    [Fact]
    public void Compute_SameContent_ProducesSameFingerprint()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        WriteFile(dirA, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(dirB, "tables/public.users.sql", "CREATE TABLE users (id int);");

        var a = SchemaFingerprint.Compute(dirA);
        var b = SchemaFingerprint.Compute(dirB);

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_DifferentContent_ProducesDifferentFingerprint()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        WriteFile(dirA, "tables/public.users.sql", "CREATE TABLE users (id int);");
        WriteFile(dirB, "tables/public.users.sql", "CREATE TABLE users (id bigint);");

        var a = SchemaFingerprint.Compute(dirA);
        var b = SchemaFingerprint.Compute(dirB);

        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_IgnoresLineEndingDifferences()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        WriteFile(dirA, "tables/public.users.sql", "CREATE TABLE users (\nid int\n);");
        WriteFile(dirB, "tables/public.users.sql", "CREATE TABLE users (\r\nid int\r\n);");

        var a = SchemaFingerprint.Compute(dirA);
        var b = SchemaFingerprint.Compute(dirB);

        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void Compute_IsOrderIndependent()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        WriteFile(dirA, "tables/a.sql", "A");
        WriteFile(dirA, "tables/b.sql", "B");
        // Write in reverse creation order; fingerprint should still match.
        WriteFile(dirB, "tables/b.sql", "B");
        WriteFile(dirB, "tables/a.sql", "A");

        var a = SchemaFingerprint.Compute(dirA);
        var b = SchemaFingerprint.Compute(dirB);

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(2, a.FileCount);
    }

    [Fact]
    public async Task FingerprintFile_RoundTrips()
    {
        WriteFile(_root, "tables/public.users.sql", "CREATE TABLE users (id int);");
        var result = SchemaFingerprint.Compute(_root);
        var path = Path.Combine(_root, "schema.fingerprint.json");

        await SchemaFingerprintFile.WriteAsync(path, result);
        var manifest = await SchemaFingerprintFile.ReadAsync(path);

        Assert.Equal(result.Fingerprint, manifest.Fingerprint);
        Assert.Equal(result.FileCount, manifest.FileCount);
        Assert.NotNull(manifest.Files);
    }

    [Fact]
    public async Task FingerprintFile_Read_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SchemaFingerprintFile.ReadAsync(Path.Combine(_root, "missing.json")));
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
