using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>watch</c> command.
/// </summary>
public sealed class WatchCommand : ICommand
{
    public string Name => "watch";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = CliParser.ParseDiffOptions(context.Args);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var watcher = new SchemaWatcher();
        var reportWriter = new SchemaDiffReportWriter();

        Console.WriteLine("Watching for schema changes. Press Ctrl+C to stop.");
        Console.WriteLine($"Left:  {Path.GetFullPath(options.LeftDirectory)}");
        Console.WriteLine($"Right: {Path.GetFullPath(options.RightDirectory)}");

        try
        {
            await watcher.WatchAsync(options, result =>
            {
                Console.WriteLine();
                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] Schema diff:");
                Console.WriteLine(reportWriter.BuildReport(result, options.Format));
                return Task.CompletedTask;
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown on Ctrl+C.
        }

        Console.WriteLine("Watch stopped.");
        return 0;
    }
}
