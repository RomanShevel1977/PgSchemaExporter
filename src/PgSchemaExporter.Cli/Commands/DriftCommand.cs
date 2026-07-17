using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Drift;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>drift</c> command.
/// </summary>
public sealed class DriftCommand : ICommand
{
    public string Name => "drift";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = CliParser.ParseDriftOptions(context.Args);

        var detector = new DriftDetector(context.MetadataProvider);
        var result = await detector.DetectAsync(options, context.Progress, context.Logger, context.CancellationToken);

        var reportWriter = new SchemaDiffReportWriter();

        // The diff report goes to stdout (may be JSON/HTML for machine consumption);
        // the human-readable drift summary goes to stderr so it never corrupts
        // structured stdout output (e.g. `drift --format json | jq`).
        Console.WriteLine(reportWriter.BuildReport(result.Diff, options.Format));

        if (result.HasDrifted)
        {
            Console.Error.WriteLine("Drift detected between the committed schema and the live database:");
            Console.Error.WriteLine($"  Missing from live DB:   {result.Missing.Count}");
            Console.Error.WriteLine($"  Unexpected in live DB:  {result.Unexpected.Count}");
            Console.Error.WriteLine($"  Modified definitions:   {result.Modified.Count}");
        }
        else
        {
            Console.Error.WriteLine("No drift detected. The live database matches the committed schema.");
        }

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            await reportWriter.WriteAsync(options.OutputFile, result.Diff, options.Format);
            Console.Error.WriteLine($"Report written to: {Path.GetFullPath(options.OutputFile)}");
        }

        return result.HasDrifted ? 2 : 0;
    }
}
