using PgSchemaExporter.Cli;
using Xunit;

namespace PgSchemaExporter.Tests;

public class CliParserV18Tests
{
    // --- plan ---

    [Fact]
    public void ParsePlanOptions_ParsesFromToAndOutput()
    {
        var (options, planFile, format) = CliParser.ParsePlanOptions(
            new[] { "--from", "./old", "--to", "./new", "--output", "plan.json" });

        Assert.Equal("./old", options.FromDirectory);
        Assert.Equal("./new", options.ToDirectory);
        Assert.Equal("plan.json", planFile);
        Assert.Equal("human", format);
        Assert.True(options.Preview);
    }

    [Fact]
    public void ParsePlanOptions_ParsesJsonFormat()
    {
        var (_, _, format) = CliParser.ParsePlanOptions(
            new[] { "--from", "./old", "--to", "./new", "--format", "json" });

        Assert.Equal("json", format);
    }

    [Fact]
    public void ParsePlanOptions_UnknownFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliParser.ParsePlanOptions(new[] { "--from", "./old", "--to", "./new", "--format", "xml" }));
    }

    [Fact]
    public void ParsePlanOptions_CapturesOnlineDdlAndTimeouts()
    {
        var (options, _, _) = CliParser.ParsePlanOptions(new[]
        {
            "--from", "./old", "--to", "./new",
            "--online-ddl", "--lock-timeout", "5s", "--statement-timeout", "1min", "--safe"
        });

        Assert.True(options.OnlineDdl);
        Assert.Equal("5s", options.LockTimeout);
        Assert.Equal("1min", options.StatementTimeout);
        Assert.True(options.Safe);
    }

    [Fact]
    public void ParsePlanOptions_UnknownArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CliParser.ParsePlanOptions(new[] { "--from", "./old", "--bogus" }));
    }

    // --- apply ---

    [Fact]
    public void ParseApplyOptions_ParsesPlanAndConnection()
    {
        var result = CliParser.ParseApplyOptions(
            new[] { "--plan", "plan.json", "--connection", "Host=localhost" });

        Assert.Equal("plan.json", result.PlanFile);
        Assert.Equal("Host=localhost", result.ConnectionString);
        Assert.False(result.Rollback);
        Assert.False(result.DryRun);
    }

    [Fact]
    public void ParseApplyOptions_ShortAliases()
    {
        var result = CliParser.ParseApplyOptions(
            new[] { "-p", "plan.json", "-c", "Host=localhost", "-y", "--rollback" });

        Assert.Equal("plan.json", result.PlanFile);
        Assert.True(result.AssumeYes);
        Assert.True(result.Rollback);
    }

    [Fact]
    public void ParseApplyOptions_DryRun_DoesNotRequireConnection()
    {
        var result = CliParser.ParseApplyOptions(new[] { "--plan", "plan.json", "--dry-run" });

        Assert.True(result.DryRun);
        Assert.Equal("", result.ConnectionString);
    }

    [Fact]
    public void ParseApplyOptions_MissingPlan_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliParser.ParseApplyOptions(new[] { "--connection", "Host=localhost" }));
        Assert.Contains("Plan file is required", ex.Message);
    }

    [Fact]
    public void ParseApplyOptions_MissingConnectionWithoutDryRun_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliParser.ParseApplyOptions(new[] { "--plan", "plan.json" }));
        Assert.Contains("Connection string is required", ex.Message);
    }
}
