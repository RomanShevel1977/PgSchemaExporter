using PgSchemaExporter.Core.Diagramming;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>diagram</c> command.
/// </summary>
public sealed class DiagramCommand : ICommand
{
    public string Name => "diagram";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = CliParser.ParseDiagramOptions(context.Args);

        var generator = new SchemaDiagramGenerator(context.MetadataProvider);
        var diagram = await generator.GenerateAsync(options, context.Progress, context.Logger, context.CancellationToken);

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            await File.WriteAllTextAsync(options.OutputFile, diagram, context.CancellationToken);
            Console.WriteLine($"Diagram written to: {Path.GetFullPath(options.OutputFile)}");
        }
        else
        {
            Console.WriteLine(diagram);
        }

        return 0;
    }
}
