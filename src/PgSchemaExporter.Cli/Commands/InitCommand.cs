using PgSchemaExporter.Core.Configuration;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>init</c> command.
/// </summary>
public sealed class InitCommand : ICommand
{
    public string Name => "init";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var (path, force) = ParseInitOptions(context.Args);

        await ExportConfigWriter.WriteAsync(path, force, context.CancellationToken);

        Console.WriteLine("Config template created.");
        Console.WriteLine($"File: {Path.GetFullPath(path)}");
        Console.WriteLine("Edit the connection string, then run: pgschema-export export --config " + path);
        return 0;
    }

    private static (string Path, bool Force) ParseInitOptions(string[] args)
    {
        var path = ExportConfigWriter.DefaultFileName;
        var force = false;

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
                case "--output":
                case "-o":
                    path = NextValue();
                    break;

                case "--force":
                    force = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return (path, force);
    }
}
