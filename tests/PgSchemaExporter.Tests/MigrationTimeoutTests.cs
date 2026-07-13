using PgSchemaExporter.Core.Migration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationTimeoutTests
{
    [Theory]
    [InlineData("5s")]
    [InlineData("30s")]
    [InlineData("1min")]
    [InlineData("500ms")]
    [InlineData("2h")]
    [InlineData("1000")]
    [InlineData("0")]
    [InlineData("100us")]
    public void IsValid_AcceptsValidTimeouts(string value)
        => Assert.True(MigrationTimeout.IsValid(value));

    [Theory]
    [InlineData("5s'; DROP TABLE users; --")]
    [InlineData("'; SELECT 1")]
    [InlineData("abc")]
    [InlineData("5 seconds")]
    [InlineData("-5s")]
    [InlineData("")]
    public void IsValid_RejectsInvalidOrMaliciousTimeouts(string value)
        => Assert.False(MigrationTimeout.IsValid(value));

    [Fact]
    public void EnsureValid_NullOrEmpty_DoesNotThrow()
    {
        MigrationTimeout.EnsureValid(null, "x");
        MigrationTimeout.EnsureValid("", "x");
        MigrationTimeout.EnsureValid("   ", "x");
    }

    [Fact]
    public void EnsureValid_Invalid_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MigrationTimeout.EnsureValid("5s'; DROP TABLE t; --", "--lock-timeout"));
        Assert.Contains("--lock-timeout", ex.Message);
    }

    [Fact]
    public void MigrationOptions_EnsureValid_RejectsInjectionTimeout()
    {
        var root = Path.Combine(Path.GetTempPath(), "pgschema-to-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new MigrationOptions
            {
                FromDirectory = root,
                ToDirectory = root,
                Preview = true,
                LockTimeout = "1s'; DROP TABLE users; --"
            };

            Assert.Throws<ArgumentException>(() => options.EnsureValid());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
