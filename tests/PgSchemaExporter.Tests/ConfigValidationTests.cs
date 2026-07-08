using PgSchemaExporter.Core.Configuration;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ConfigValidationTests : IDisposable
{
    private readonly string _root;

    public ConfigValidationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-cfg-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFound()
    {
        var path = Path.Combine(_root, "does-not-exist.json");

        await Assert.ThrowsAsync<FileNotFoundException>(() => ExportConfigLoader.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ThrowsConfigValidation()
    {
        var path = Path.Combine(_root, "empty.json");
        await File.WriteAllTextAsync(path, "   ");

        await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ReportsLineInformation()
    {
        var path = Path.Combine(_root, "broken.json");
        await File.WriteAllTextAsync(path, "{ \"connectionString\": \"x\", ");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(path));

        Assert.Single(ex.Errors);
        Assert.Contains("Invalid JSON", ex.Errors[0]);
    }

    [Fact]
    public async Task LoadAsync_MissingConnectionString_ReportsActionableError()
    {
        var path = Path.Combine(_root, "no-conn.json");
        await File.WriteAllTextAsync(path, "{ \"outputDirectory\": \"./db\" }");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(path));

        Assert.Contains(ex.Errors, e => e.Contains("Connection string is required"));
    }

    [Fact]
    public void Validate_CollectsAllProblemsAtOnce()
    {
        var options = new ExportOptions
        {
            ConnectionString = "",
            OutputDirectory = "",
            Schemas = []
        };

        var errors = options.Validate();

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var options = new ExportOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
            OutputDirectory = "./db",
            Schemas = ["public"]
        };

        Assert.Empty(options.Validate());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
