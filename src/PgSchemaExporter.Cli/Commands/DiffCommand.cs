using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>diff</c> command.
/// </summary>
public sealed class DiffCommand : ICommand
{
    public string Name => "diff";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = CliParser.ParseDiffOptions(context.Args);

        var differ = new SchemaDiffer(context.MetadataProvider);
        SchemaDiffResult result;

        if (!string.IsNullOrWhiteSpace(options.LeftConnectionString) ||
            !string.IsNullOrWhiteSpace(options.RightConnectionString))
        {
            result = await differ.DiffAsync(options, context.Progress, context.Logger, context.CancellationToken);
        }
        else
        {
            result = differ.Diff(options);
        }

        var reportWriter = new SchemaDiffReportWriter();
        Console.WriteLine(reportWriter.BuildReport(result, options.Format));

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            await reportWriter.WriteAsync(options.OutputFile, result, options.Format);
            Console.WriteLine($"Report written to: {Path.GetFullPath(options.OutputFile)}");
        }

        return result.HasDifferences ? 2 : 0;
    }
}
