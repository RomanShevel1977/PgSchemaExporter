using PgSchemaExporter.Core.Integrity;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaFingerprintCompareTests : IDisposable
{
    private readonly string _dir;

    public SchemaFingerprintCompareTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pgschema-fp-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(_dir, "tables"));
    }

    [Fact]
    public void CompareFiles_DetectsAddedRemovedModified()
    {
        Write("tables/a.sql", "CREATE TABLE a (id int);");
        Write("tables/b.sql", "CREATE TABLE b (id int);");
        var baseline = SchemaFingerprint.Compute(_dir);

        // Modify a, remove b, add c.
        Write("tables/a.sql", "CREATE TABLE a (id bigint);");
        File.Delete(Path.Combine(_dir, "tables", "b.sql"));
        Write("tables/c.sql", "CREATE TABLE c (id int);");
        var current = SchemaFingerprint.Compute(_dir);

        var comparison = SchemaFingerprint.CompareFiles(baseline.Files, current);

        Assert.True(comparison.HasDifferences);
        Assert.Contains("tables/c.sql", comparison.Added);
        Assert.Contains("tables/b.sql", comparison.Removed);
        Assert.Contains("tables/a.sql", comparison.Modified);
    }

    [Fact]
    public void CompareFiles_NoChanges_HasNoDifferences()
    {
        Write("tables/a.sql", "CREATE TABLE a (id int);");
        var baseline = SchemaFingerprint.Compute(_dir);
        var current = SchemaFingerprint.Compute(_dir);

        var comparison = SchemaFingerprint.CompareFiles(baseline.Files, current);

        Assert.False(comparison.HasDifferences);
    }

    [Fact]
    public void CompareFiles_NullExpected_ReturnsEmpty()
    {
        Write("tables/a.sql", "CREATE TABLE a (id int);");
        var current = SchemaFingerprint.Compute(_dir);

        var comparison = SchemaFingerprint.CompareFiles(null, current);

        Assert.False(comparison.HasDifferences);
    }

    [Fact]
    public void Compute_IgnoresLineEndingDifferences()
    {
        Write("tables/a.sql", "CREATE TABLE a (id int);\nLINE2");
        var lf = SchemaFingerprint.Compute(_dir);

        Write("tables/a.sql", "CREATE TABLE a (id int);\r\nLINE2");
        var crlf = SchemaFingerprint.Compute(_dir);

        Assert.Equal(lf.Fingerprint, crlf.Fingerprint);
    }

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
