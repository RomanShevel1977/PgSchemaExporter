using PgSchemaExporter.Core.Configuration;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ExportConfigLoaderIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public ExportConfigLoaderIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pgschema-config-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ValidConfig_ReturnsOptions()
    {
        var configPath = Path.Combine(_tempRoot, "valid.json");
        await File.WriteAllTextAsync(configPath, @"
{
    ""connectionString"": ""Host=localhost;Database=db"",
    ""outputDirectory"": ""./schema"",
    ""schemas"": [""public"", ""app""],
    ""excludeSchemas"": [""pg_catalog""],
    ""cleanOutputDirectory"": true,
    ""parallel"": true,
    ""include"": { ""tables"": true, ""views"": true },
    ""format"": { ""useIfNotExists"": false }
}");

        var options = await ExportConfigLoader.LoadAsync(configPath);

        Assert.Equal("Host=localhost;Database=db", options.ConnectionString);
        Assert.Equal("./schema", options.OutputDirectory);
        Assert.True(options.CleanOutputDirectory);
        Assert.True(options.Parallel);
        Assert.Equal(["public", "app"], options.Schemas);
        Assert.Equal("pg_catalog", options.ExcludeSchemas[0]);
        Assert.NotNull(options.Include);
        Assert.NotNull(options.Format);
        Assert.False(options.Format.UseIfNotExists);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var configPath = Path.Combine(_tempRoot, "missing.json");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains(configPath, ex.Message);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "empty.json");
        await File.WriteAllTextAsync(configPath, "   ");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("empty", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{ connectionString: ");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("Invalid JSON", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MissingConnectionString_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "missing-connection.json");
        await File.WriteAllTextAsync(configPath, @"{ ""schemas"": [""public""], ""include"": {}, ""format"": {} }");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("Connection string", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MissingSchemas_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "missing-schemas.json");
        await File.WriteAllTextAsync(configPath, @"{ ""connectionString"": ""x"", ""schemas"": null, ""include"": {}, ""format"": {} }");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("schema", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_EmptySchemasEntry_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "empty-schemas.json");
        await File.WriteAllTextAsync(configPath, @"{ ""connectionString"": ""x"", ""schemas"": [""""], ""include"": {}, ""format"": {} }");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("empty", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MissingInclude_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "missing-include.json");
        await File.WriteAllTextAsync(configPath, @"{ ""connectionString"": ""x"", ""schemas"": [""public""], ""include"": null, ""format"": {} }");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("'include'", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MissingFormat_ThrowsConfigValidationException()
    {
        var configPath = Path.Combine(_tempRoot, "missing-format.json");
        await File.WriteAllTextAsync(configPath, @"{ ""connectionString"": ""x"", ""schemas"": [""public""], ""include"": {}, ""format"": null }");

        var ex = await Assert.ThrowsAsync<ConfigValidationException>(() => ExportConfigLoader.LoadAsync(configPath));
        Assert.Contains("'format'", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_TrailingCommentsAndCommas_AreTolerated()
    {
        var configPath = Path.Combine(_tempRoot, "tolerant.json");
        await File.WriteAllTextAsync(configPath, @"{
    ""connectionString"": ""Host=localhost"",
    ""schemas"": [""public""],
    ""include"": {},
    ""format"": {},
}");

        var options = await ExportConfigLoader.LoadAsync(configPath);
        Assert.Equal("Host=localhost", options.ConnectionString);
    }
}
