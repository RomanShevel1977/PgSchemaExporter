using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Hazards;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>migrate</c> command.
/// </summary>
public sealed class MigrateCommand : ICommand
{
    public string Name => "migrate";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = ParseMigrateOptions(context.Args);

        var generator = new MigrationGenerator();
        var script = generator.Generate(options);

        if (!script.HasChanges)
        {
            Console.WriteLine("No schema changes detected. No migration was generated.");
            return 0;
        }

        if (options.WarnHazards)
            PrintHazards(HazardAnalyzer.Analyze(script));

        if (options.Preview)
        {
            var renderOptions = options.ToRenderOptions();
            Console.WriteLine(script.RenderUp(renderOptions));
            Console.WriteLine("-- ----------------------------------------------------------------");
            Console.WriteLine(script.RenderDown(renderOptions));
        }
        else
        {
            var writer = new MigrationWriter();
            var timestamp = DateTimeOffset.UtcNow;
            var result = await writer.WriteAsync(options, script, timestamp, context.CancellationToken);

            await MigrationHistory.AppendAsync(options.OutputDirectory, new MigrationHistoryEntry
            {
                AppliedAt = timestamp,
                Name = options.Name,
                UpFile = Path.GetFileName(result.UpFile),
                DownFile = Path.GetFileName(result.DownFile),
                UpStatements = script.Up.Count,
                DownStatements = script.Down.Count,
                Destructive = script.HasDestructiveChanges
            }, context.CancellationToken);

            Console.WriteLine("Migration generated.");
            Console.WriteLine($"Up:   {Path.GetFullPath(result.UpFile)}");
            Console.WriteLine($"Down: {Path.GetFullPath(result.DownFile)}");
            Console.WriteLine($"Statements: {script.Up.Count} up / {script.Down.Count} down");
            Console.WriteLine($"History: {Path.GetFullPath(Path.Combine(options.OutputDirectory, MigrationHistory.DefaultFileName))}");
        }

        if (script.HasDestructiveChanges)
        {
            Console.WriteLine(options.Safe
                ? "Note: destructive statements were emitted as comments (--safe). Review before running."
                : "Warning: this migration contains destructive statements. Review before running.");
        }

        return 0;
    }

    private static MigrationOptions ParseMigrateOptions(string[] args)
    {
        var options = new MigrationOptions();

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
                case "--from":
                case "-f":
                    options.FromDirectory = NextValue();
                    break;

                case "--to":
                case "-t":
                    options.ToDirectory = NextValue();
                    break;

                case "--output":
                case "-o":
                    options.OutputDirectory = NextValue();
                    break;

                case "--name":
                case "-n":
                    options.Name = NextValue();
                    break;

                case "--safe":
                    options.Safe = true;
                    break;

                case "--preview":
                    options.Preview = true;
                    break;

                case "--online-ddl":
                    options.OnlineDdl = true;
                    break;

                case "--lock-timeout":
                    options.LockTimeout = NextValue();
                    break;

                case "--statement-timeout":
                    options.StatementTimeout = NextValue();
                    break;

                case "--warn-hazards":
                    options.WarnHazards = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;
    }

    private static void PrintHazards(IReadOnlyList<Hazard> hazards)
    {
        if (hazards.Count == 0)
            return;

        static int Rank(string severity) => severity.ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };

        Console.WriteLine($"Hazards detected ({hazards.Count}):");
        foreach (var hazard in hazards.OrderByDescending(h => Rank(h.Severity.ToString())))
        {
            Console.WriteLine($"  [{hazard.Severity.ToString().ToUpperInvariant()}] {hazard.Category}: {hazard.Message}");
            Console.WriteLine($"      {hazard.Statement}");
        }
        Console.WriteLine();
    }
}
