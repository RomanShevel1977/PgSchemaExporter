using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>export</c> command.
/// </summary>
public sealed class ExportCommand : ICommand
{
    public string Name => "export";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = await ParseExportOptionsAsync(context.Args, context.CancellationToken);

        var exporter = new SchemaExporter(
            context.MetadataProvider,
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var summary = await exporter.ExportAsync(options, context.Progress, context.Logger, context.CancellationToken);

        if (summary.DryRun)
        {
            Console.WriteLine("Dry run completed. No files were written.");
            Console.WriteLine($"Would export to: {Path.GetFullPath(summary.OutputDirectory)}");
            Console.WriteLine($"Objects discovered: {summary.TotalObjects}");
            foreach (var (kind, count) in summary.Counts.Where(c => c.Count > 0))
                Console.WriteLine($"  {kind}: {count}");
        }
        else
        {
            Console.WriteLine("Schema export completed.");
            Console.WriteLine($"Output: {Path.GetFullPath(summary.OutputDirectory)}");
            Console.WriteLine($"Objects exported: {summary.TotalObjects}");
            Console.WriteLine("Deployment files were generated in the output directory.");
        }

        return 0;
    }

    private static async Task<ExportOptions> ParseExportOptionsAsync(string[] args, CancellationToken cancellationToken)
    {
        ExportOptions? options = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --config");

                options = await PgSchemaExporter.Core.Configuration.ExportConfigLoader.LoadAsync(args[i + 1], cancellationToken);
                break;
            }
        }

        options ??= new ExportOptions();

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
                case "--config":
                    i++;
                    break;

                case "--connection":
                case "-c":
                    options.ConnectionString = NextValue();
                    break;

                case "--output":
                case "-o":
                    options.OutputDirectory = NextValue();
                    break;

                case "--schemas":
                    options.Schemas = Split(NextValue());
                    break;

                case "--exclude-schemas":
                    options.ExcludeSchemas = Split(NextValue());
                    break;

                case "--clean":
                    options.CleanOutputDirectory = true;
                    break;

                case "--dry-run":
                    options.DryRun = true;
                    break;

                case "--parallel":
                    options.Parallel = true;
                    break;

                default:
                    if (arg.StartsWith("--include-", StringComparison.Ordinal))
                    {
                        ApplyIncludeToggle(options.Include, arg["--include-".Length..], true);
                        break;
                    }

                    if (arg.StartsWith("--exclude-", StringComparison.Ordinal))
                    {
                        ApplyIncludeToggle(options.Include, arg["--exclude-".Length..], false);
                        break;
                    }

                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;
    }

    private static void ApplyIncludeToggle(IncludeOptions include, string name, bool value)
    {
        switch (name)
        {
            case "schemas": include.Schemas = value; break;
            case "extensions": include.Extensions = value; break;
            case "types": include.Types = value; break;
            case "sequences": include.Sequences = value; break;
            case "domains": include.Domains = value; break;
            case "foreign-tables": include.ForeignTables = value; break;
            case "tables": include.Tables = value; break;
            case "constraints": include.Constraints = value; break;
            case "indexes": include.Indexes = value; break;
            case "views": include.Views = value; break;
            case "triggers": include.Triggers = value; break;
            case "event-triggers": include.EventTriggers = value; break;
            case "rules": include.Rules = value; break;
            case "aggregates": include.Aggregates = value; break;
            case "operators": include.Operators = value; break;
            case "casts": include.Casts = value; break;
            case "publications": include.Publications = value; break;
            case "subscriptions": include.Subscriptions = value; break;
            case "policies": include.Policies = value; break;
            case "comments": include.Comments = value; break;
            case "grants": include.Grants = value; break;
            case "functions": include.Functions = value; break;
            default:
                throw new ArgumentException($"Unknown object kind: {name}");
        }
    }

    private static string[] Split(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
