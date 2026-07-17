using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>split-dump</c> command.
/// </summary>
public sealed class SplitDumpCommand : ICommand
{
    public string Name => "split-dump";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = ParseSplitDumpOptions(context.Args);

        var splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await splitter.SplitAsync(options, context.CancellationToken);

        Console.WriteLine("Dump split completed.");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");
        Console.WriteLine("Deployment files were generated in the output directory.");
        return 0;
    }

    private static SplitDumpOptions ParseSplitDumpOptions(string[] args)
    {
        var options = new SplitDumpOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            string NextValue()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}");

                return args[++i];
            }

            switch (arg)
            {
                case "--input":
                case "-i":
                    options.InputFile = NextValue();
                    break;

                case "--output":
                case "-o":
                    options.OutputDirectory = NextValue();
                    break;

                case "--clean":
                    options.CleanOutputDirectory = true;
                    break;

                case "--no-deploy":
                    options.GenerateDeployScript = false;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;
    }
}
