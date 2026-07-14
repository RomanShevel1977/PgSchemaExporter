using PgSchemaExporter.Cli;
using PgSchemaExporter.Core.Diagramming;
using Xunit;

namespace PgSchemaExporter.Tests;

public class CliParserV19Tests
{
    [Fact]
    public void ParseDiagramOptions_FromConnection_ParsesFormatAndOutput()
    {
        var options = CliParser.ParseDiagramOptions(new[]
        {
            "--connection", "Host=localhost;Database=db",
            "--output", "diagram.dot",
            "--schemas", "public,app"
        });

        Assert.Equal("Host=localhost;Database=db", options.ConnectionString);
        Assert.Equal("diagram.dot", options.OutputFile);
        Assert.Equal(DiagramFormat.Dot, options.Format);
        Assert.Equal(["public", "app"], options.Schemas);
    }

    [Fact]
    public void ParseDiagramOptions_FromSchema_ParsesFormat()
    {
        var options = CliParser.ParseDiagramOptions(new[]
        {
            "-s", "./db-schema",
            "--format", "mermaid"
        });

        Assert.Equal("./db-schema", options.SchemaDirectory);
        Assert.Equal(DiagramFormat.Mermaid, options.Format);
    }

    [Fact]
    public void ParseDiagramOptions_InfersFormatFromOutputExtension()
    {
        var options = CliParser.ParseDiagramOptions(new[]
        {
            "-s", "./db-schema",
            "-o", "schema.mmd"
        });

        Assert.Equal(DiagramFormat.Mermaid, options.Format);
    }

    [Fact]
    public void ParseDiagramOptions_UnknownFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliParser.ParseDiagramOptions(new[] { "--connection", "cs", "--format", "svg" }));
    }

    [Fact]
    public void ParseDiagramOptions_UnknownArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliParser.ParseDiagramOptions(new[] { "--schema", "./db", "--bogus" }));
    }
}
