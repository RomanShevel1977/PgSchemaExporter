using PgSchemaExporter.Cli;
using PgSchemaExporter.Core.Options;
using Xunit;

namespace PgSchemaExporter.Tests;

public class CliParserV17Tests
{
    // --- drift ---

    [Fact]
    public void ParseDriftOptions_ParsesSchemaAndConnection()
    {
        var args = new[] { "--schema", "./db-schema", "--connection", "Host=localhost" };

        var options = CliParser.ParseDriftOptions(args);

        Assert.Equal("./db-schema", options.SchemaDirectory);
        Assert.Equal("Host=localhost", options.ConnectionString);
    }

    [Fact]
    public void ParseDriftOptions_ShortAliases_Work()
    {
        var args = new[] { "-s", "./db-schema", "-c", "Host=localhost", "-o", "report.json" };

        var options = CliParser.ParseDriftOptions(args);

        Assert.Equal("./db-schema", options.SchemaDirectory);
        Assert.Equal("Host=localhost", options.ConnectionString);
        Assert.Equal("report.json", options.OutputFile);
    }

    [Fact]
    public void ParseDriftOptions_InfersJsonFormatFromOutput()
    {
        var args = new[] { "--schema", "./db", "--connection", "c", "--output", "report.json" };

        var options = CliParser.ParseDriftOptions(args);

        Assert.Equal(DiffFormat.Json, options.Format);
    }

    [Fact]
    public void ParseDriftOptions_InfersHtmlFormatFromOutput()
    {
        var args = new[] { "--schema", "./db", "--connection", "c", "--output", "report.html" };

        var options = CliParser.ParseDriftOptions(args);

        Assert.Equal(DiffFormat.Html, options.Format);
    }

    [Fact]
    public void ParseDriftOptions_ExplicitFormatWins()
    {
        var args = new[] { "--schema", "./db", "--connection", "c", "--format", "text", "--output", "report.json" };

        var options = CliParser.ParseDriftOptions(args);

        Assert.Equal(DiffFormat.Text, options.Format);
    }

    [Fact]
    public void ParseDriftOptions_EmptySchemas_Throws()
    {
        var args = new[] { "--schema", "./db", "--connection", "c", "--schemas", "" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDriftOptions(args));
        Assert.Contains("--schemas cannot be empty", ex.Message);
    }

    [Fact]
    public void ParseDriftOptions_AllFlags_Parse()
    {
        var args = new[]
        {
            "--schema", "./db",
            "--connection", "c",
            "--schemas", "public,app",
            "--exclude-schemas", "pg_catalog",
            "--parallel",
            "--ignore-comments",
            "--ignore-whitespace",
            "--context"
        };

        var options = CliParser.ParseDriftOptions(args);

        Assert.Equal(["public", "app"], options.Schemas);
        Assert.Equal(["pg_catalog"], options.ExcludeSchemas);
        Assert.True(options.Parallel);
        Assert.True(options.IgnoreComments);
        Assert.True(options.IgnoreWhitespace);
        Assert.True(options.ShowContext);
    }

    [Fact]
    public void ParseDriftOptions_UnknownArgument_Throws()
    {
        var args = new[] { "--schema", "./db", "--connection", "c", "--bogus" };

        Assert.Throws<ArgumentException>(() => CliParser.ParseDriftOptions(args));
    }

    [Fact]
    public void ParseDriftOptions_MissingValue_Throws()
    {
        var args = new[] { "--schema" };

        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseDriftOptions(args));
        Assert.Contains("Missing value for --schema", ex.Message);
    }

    [Fact]
    public void DriftOptions_ToDiffOptions_MapsSchemaAsLeftAndDbAsRight()
    {
        var args = new[] { "--schema", "./db-schema", "--connection", "Host=localhost", "--context" };
        var options = CliParser.ParseDriftOptions(args);

        var diff = options.ToDiffOptions();

        Assert.Equal("./db-schema", diff.LeftDirectory);
        Assert.Equal("Host=localhost", diff.RightConnectionString);
        Assert.True(diff.ShowContext);
    }

    // --- fingerprint ---

    [Fact]
    public void ParseFingerprintOptions_ParsesSchema()
    {
        var (schema, output, verify) = CliParser.ParseFingerprintOptions(new[] { "--schema", "./db" });

        Assert.Equal("./db", schema);
        Assert.Null(output);
        Assert.Null(verify);
    }

    [Fact]
    public void ParseFingerprintOptions_ParsesOutputAndVerify()
    {
        var (schema, output, verify) = CliParser.ParseFingerprintOptions(
            new[] { "-s", "./db", "-o", "fp.json", "--verify", "expected.json" });

        Assert.Equal("./db", schema);
        Assert.Equal("fp.json", output);
        Assert.Equal("expected.json", verify);
    }

    [Fact]
    public void ParseFingerprintOptions_MissingSchema_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliParser.ParseFingerprintOptions(Array.Empty<string>()));
        Assert.Contains("Schema directory is required", ex.Message);
    }

    [Fact]
    public void ParseFingerprintOptions_UnknownArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliParser.ParseFingerprintOptions(new[] { "--schema", "./db", "--nope" }));
    }
}
