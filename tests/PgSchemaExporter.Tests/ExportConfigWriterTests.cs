using System.Text.Json;
using PgSchemaExporter.Core.Configuration;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ExportConfigWriterTests : IDisposable
{
    private readonly string _root;

    public ExportConfigWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-init-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void BuildTemplate_ProducesValidJsonWithExpectedKeys()
    {
        var json = ExportConfigWriter.BuildTemplate();

        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(parsed.TryGetProperty("connectionString", out _));
        Assert.True(parsed.TryGetProperty("outputDirectory", out _));
        Assert.True(parsed.TryGetProperty("parallel", out var parallel));
        Assert.False(parallel.GetBoolean());
        Assert.True(parsed.TryGetProperty("include", out _));
    }

    [Fact]
    public async Task WriteAsync_CreatesFile()
    {
        var path = Path.Combine(_root, "pgschema-export.json");

        await ExportConfigWriter.WriteAsync(path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteAsync_ThrowsWhenFileExistsWithoutForce()
    {
        var path = Path.Combine(_root, "pgschema-export.json");
        await ExportConfigWriter.WriteAsync(path);

        await Assert.ThrowsAsync<IOException>(() => ExportConfigWriter.WriteAsync(path));
    }

    [Fact]
    public async Task WriteAsync_OverwritesWhenForced()
    {
        var path = Path.Combine(_root, "pgschema-export.json");
        await File.WriteAllTextAsync(path, "stale");

        await ExportConfigWriter.WriteAsync(path, overwrite: true);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("connectionString", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
