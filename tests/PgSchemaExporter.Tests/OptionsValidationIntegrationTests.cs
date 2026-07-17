using PgSchemaExporter.Core.Diagramming;
using PgSchemaExporter.Core.Drift;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class OptionsValidationIntegrationTests
{
    [Fact]
    public void ExportOptions_EnsureValidForExport_ValidOptions_NormalizesSchemas()
    {
        var options = new ExportOptions
        {
            ConnectionString = "Host=localhost;Database=db",
            OutputDirectory = "./out",
            Schemas = ["  PUBLIC  ", " app "],
            Include = new IncludeOptions(),
            Format = new FormatOptions()
        };

        options.EnsureValidForExport();

        Assert.Equal(["PUBLIC", "app"], options.EffectiveSchemas);
    }

    [Fact]
    public void ExportOptions_EnsureValidForExport_MissingConnectionString_ThrowsArgumentException()
    {
        var options = new ExportOptions
        {
            ConnectionString = "",
            OutputDirectory = "./out",
            Schemas = ["public"],
            Include = new IncludeOptions(),
            Format = new FormatOptions()
        };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValidForExport);
        Assert.Contains("Connection string", ex.Message);
    }

    [Fact]
    public void ExportOptions_EnsureValidForExport_MissingOutputDirectory_ThrowsArgumentException()
    {
        var options = new ExportOptions
        {
            ConnectionString = "x",
            OutputDirectory = "  ",
            Schemas = ["public"],
            Include = new IncludeOptions(),
            Format = new FormatOptions()
        };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValidForExport);
        Assert.Contains("Output directory", ex.Message);
    }

    [Fact]
    public void ExportOptions_EnsureValidForExport_EmptySchemas_ThrowsArgumentException()
    {
        var options = new ExportOptions
        {
            ConnectionString = "x",
            OutputDirectory = "./out",
            Schemas = [],
            Include = new IncludeOptions(),
            Format = new FormatOptions()
        };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValidForExport);
        Assert.Contains("schema", ex.Message);
    }

    [Fact]
    public void SchemaDiffOptions_EnsureValid_MissingLeftAndLeftDb_ThrowsArgumentException()
    {
        var options = new SchemaDiffOptions { RightDirectory = _TempDir() };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("--left", ex.Message);
    }

    [Fact]
    public void SchemaDiffOptions_EnsureValid_MissingRightAndRightDb_ThrowsArgumentException()
    {
        var options = new SchemaDiffOptions { LeftDirectory = _TempDir() };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("--right", ex.Message);
    }

    [Fact]
    public void SchemaDiffOptions_EnsureValid_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var options = new SchemaDiffOptions
        {
            LeftDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")),
            RightDirectory = _TempDir()
        };

        Assert.Throws<DirectoryNotFoundException>(options.EnsureValid);
    }

    [Fact]
    public void MigrationOptions_EnsureValid_MissingFromDirectory_ThrowsArgumentException()
    {
        var options = new MigrationOptions { ToDirectory = _TempDir() };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("--from", ex.Message);
    }

    [Fact]
    public void MigrationOptions_EnsureValid_MissingToDirectory_ThrowsArgumentException()
    {
        var options = new MigrationOptions { FromDirectory = _TempDir() };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("--to", ex.Message);
    }

    [Fact]
    public void MigrationOptions_EnsureValid_MissingFromDirectoryPath_ThrowsDirectoryNotFoundException()
    {
        var options = new MigrationOptions
        {
            FromDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")),
            ToDirectory = _TempDir()
        };

        Assert.Throws<DirectoryNotFoundException>(options.EnsureValid);
    }

    [Fact]
    public void MigrationOptions_EnsureValid_Preview_DoesNotRequireOutputDirectory()
    {
        var options = new MigrationOptions
        {
            FromDirectory = _TempDir(),
            ToDirectory = _TempDir(),
            Preview = true,
            OutputDirectory = ""
        };

        options.EnsureValid();
        Assert.True(options.Preview);
    }

    [Fact]
    public void MigrationOptions_EnsureValid_InvalidTimeout_ThrowsArgumentException()
    {
        var options = new MigrationOptions
        {
            FromDirectory = _TempDir(),
            ToDirectory = _TempDir(),
            LockTimeout = "abc"
        };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("Invalid timeout", ex.Message);
    }

    [Fact]
    public void MigrationTimeout_EnsureValid_NullValue_Passes()
    {
        MigrationTimeout.EnsureValid(null, "--lock-timeout");
        MigrationTimeout.EnsureValid("", "--lock-timeout");
        MigrationTimeout.EnsureValid("   ", "--lock-timeout");
    }

    [Fact]
    public void MigrationTimeout_IsValid_AcceptsValidTimeouts()
    {
        Assert.True(MigrationTimeout.IsValid("5s"));
        Assert.True(MigrationTimeout.IsValid("30s"));
        Assert.True(MigrationTimeout.IsValid("1min"));
        Assert.True(MigrationTimeout.IsValid("500ms"));
        Assert.True(MigrationTimeout.IsValid("1000"));
    }

    [Fact]
    public void MigrationTimeout_IsValid_RejectsInvalidTimeouts()
    {
        Assert.False(MigrationTimeout.IsValid("abc"));
        Assert.False(MigrationTimeout.IsValid("5x"));
        Assert.False(MigrationTimeout.IsValid("5minutes"));
        Assert.False(MigrationTimeout.IsValid(""));
    }

    [Fact]
    public void DiagramOptions_EnsureValid_BothSources_ThrowsArgumentException()
    {
        var options = new DiagramOptions
        {
            ConnectionString = "x",
            SchemaDirectory = _TempDir()
        };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("exactly one source", ex.Message);
    }

    [Fact]
    public void DiagramOptions_EnsureValid_NoSource_ThrowsArgumentException()
    {
        var options = new DiagramOptions();

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("exactly one source", ex.Message);
    }

    [Fact]
    public void DiagramOptions_EnsureValid_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var options = new DiagramOptions
        {
            SchemaDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"))
        };

        Assert.Throws<DirectoryNotFoundException>(options.EnsureValid);
    }

    [Fact]
    public void DriftOptions_EnsureValid_MissingSchemaDirectory_ThrowsArgumentException()
    {
        var options = new DriftOptions { ConnectionString = "x" };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("--schema", ex.Message);
    }

    [Fact]
    public void DriftOptions_EnsureValid_MissingConnectionString_ThrowsArgumentException()
    {
        var options = new DriftOptions { SchemaDirectory = _TempDir() };

        var ex = Assert.Throws<ArgumentException>(options.EnsureValid);
        Assert.Contains("--connection", ex.Message);
    }

    [Fact]
    public void DriftOptions_EnsureValid_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var options = new DriftOptions
        {
            SchemaDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")),
            ConnectionString = "x"
        };

        Assert.Throws<DirectoryNotFoundException>(options.EnsureValid);
    }

    private static string _TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "pgschema-opts-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
