using Microsoft.Extensions.Logging;
using PgSchemaExporter.Cli;
using PgSchemaExporter.Cli.Diagnostics;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Configuration;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;

const string VersionString = "1.6.0";

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return;
}

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine($"pgschema-export {VersionString}");
    return;
}

// Extract global verbosity flags before command-specific parsing.
var verbosity = ResolveVerbosity(args);
args = args.Where(a => a is not "--verbose" and not "--quiet").ToArray();

IProgressReporter progress = new ConsoleProgressReporter(verbosity);
ILogger logger = new ConsoleLogger(verbosity);

try
{
    var command = args[0];

    if (string.Equals(command, "export", StringComparison.OrdinalIgnoreCase))
    {
        var options = await ParseExportOptionsAsync(args.Skip(1).ToArray());

        var exporter = new SchemaExporter(
            new PostgresMetadataProvider(),
            new SchemaFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        var summary = await exporter.ExportAsync(options, progress, logger);

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
        return;
    }

    if (string.Equals(command, "diff", StringComparison.OrdinalIgnoreCase))
    {
        var options = ParseDiffOptions(args.Skip(1).ToArray());

        var differ = new SchemaDiffer();
        SchemaDiffResult result;

        if (!string.IsNullOrWhiteSpace(options.LeftConnectionString) ||
            !string.IsNullOrWhiteSpace(options.RightConnectionString))
        {
            result = await differ.DiffAsync(options, progress, logger);
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

        Environment.ExitCode = result.HasDifferences ? 2 : 0;
        return;
    }

    if (string.Equals(command, "migrate", StringComparison.OrdinalIgnoreCase))
    {
        var options = ParseMigrateOptions(args.Skip(1).ToArray());

        var generator = new MigrationGenerator();
        var script = generator.Generate(options);

        if (!script.HasChanges)
        {
            Console.WriteLine("No schema changes detected. No migration was generated.");
            return;
        }

        if (options.Preview)
        {
            Console.WriteLine(script.RenderUp(options.Safe));
            Console.WriteLine("-- ----------------------------------------------------------------");
            Console.WriteLine(script.RenderDown(options.Safe));
        }
        else
        {
            var writer = new MigrationWriter();
            var result = await writer.WriteAsync(options, script, DateTimeOffset.UtcNow);
            Console.WriteLine("Migration generated.");
            Console.WriteLine($"Up:   {Path.GetFullPath(result.UpFile)}");
            Console.WriteLine($"Down: {Path.GetFullPath(result.DownFile)}");
            Console.WriteLine($"Statements: {script.Up.Count} up / {script.Down.Count} down");
        }

        if (script.HasDestructiveChanges)
        {
            Console.WriteLine(options.Safe
                ? "Note: destructive statements were emitted as comments (--safe). Review before running."
                : "Warning: this migration contains destructive statements. Review before running.");
        }
        return;
    }

    if (string.Equals(command, "split-dump", StringComparison.OrdinalIgnoreCase))
    {
        var options = ParseSplitDumpOptions(args.Skip(1).ToArray());

        var splitter = new DumpSplitter(
            new SqlStatementSplitter(),
            new PgDumpObjectClassifier(),
            new DumpSplitFileWriter(),
            new DeployScriptWriter(),
            new ReadmeWriter());

        await splitter.SplitAsync(options);

        Console.WriteLine("Dump split completed.");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");
        Console.WriteLine("Deployment files were generated in the output directory.");
        return;
    }

    if (string.Equals(command, "init", StringComparison.OrdinalIgnoreCase))
    {
        var (path, force) = ParseInitOptions(args.Skip(1).ToArray());

        await ExportConfigWriter.WriteAsync(path, force);

        Console.WriteLine("Config template created.");
        Console.WriteLine($"File: {Path.GetFullPath(path)}");
        Console.WriteLine("Edit the connection string, then run: pgschema-export export --config " + path);
        return;
    }

    if (string.Equals(command, "watch", StringComparison.OrdinalIgnoreCase))
    {
        var options = ParseDiffOptions(args.Skip(1).ToArray());

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
        return;
    }

    Console.Error.WriteLine($"Unknown command: {command}");
    PrintHelp();
    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled.");
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    var (message, suggestion) = FriendlyError.Describe(ex);
    logger.LogError(ex, "Operation failed");

    Console.Error.WriteLine("Operation failed:");
    Console.Error.WriteLine($"  {message}");
    if (!string.IsNullOrEmpty(suggestion))
        Console.Error.WriteLine($"  Suggestion: {suggestion}");

    Environment.ExitCode = 1;
}

static async Task<ExportOptions> ParseExportOptionsAsync(string[] args)
{
    ExportOptions? options = null;

    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is "--config")
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException("Missing value for --config");

            options = await ExportConfigLoader.LoadAsync(args[i + 1]);
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

static SplitDumpOptions ParseSplitDumpOptions(string[] args)
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

static MigrationOptions ParseMigrateOptions(string[] args)
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

            default:
                throw new ArgumentException($"Unknown argument: {arg}");
        }
    }

    return options;
}

static SchemaDiffOptions ParseDiffOptions(string[] args)
{
    return CliParser.ParseDiffOptions(args);
}

static (string Path, bool Force) ParseInitOptions(string[] args)
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

static void ApplyIncludeToggle(IncludeOptions include, string name, bool value)
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

static string[] Split(string value)
{
    return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static Verbosity ResolveVerbosity(string[] args)
{
    // --quiet wins over --verbose if both are supplied.
    if (args.Contains("--quiet"))
        return Verbosity.Quiet;

    return args.Contains("--verbose") ? Verbosity.Verbose : Verbosity.Normal;
}

static void PrintHelp()
{
    Console.WriteLine("""
PostgreSQL Git-Native Schema Exporter 1.6.0

Usage:
  pgschema-export init [--output "./pgschema-export.json"]
  pgschema-export export --connection "<connection-string>" --output "./db-schema"
  pgschema-export split-dump --input "./schema.sql" --output "./db-schema"
  pgschema-export diff --left "./old-schema" --right "./new-schema"
  pgschema-export watch --left "./old-schema" --right "./new-schema"
  pgschema-export migrate --from "./old-schema" --to "./new-schema" --output "./migrations"

Commands:
  init         Create a pgschema-export.json config template
  export       Live export from PostgreSQL using pg_catalog/information_schema
  split-dump   Split existing pg_dump schema-only SQL file into folders
  diff         Compare two exported schema directories and report changes
  watch        Continuously re-run a directory diff as files change
  migrate      Generate up/down migration scripts between two exported schemas

Global options:
      --verbose          Print detailed per-object progress and debug logs (to stderr)
      --quiet            Suppress progress output; only errors are shown
      --version, -v      Show the installed version
      --help, -h         Show this help

Init options:
  -o, --output           Path for the config file. Default: pgschema-export.json
      --force            Overwrite an existing config file

Export options:
  -c, --connection       PostgreSQL connection string
  -o, --output           Output directory
      --schemas          Comma-separated schemas. Default: public
      --exclude-schemas  Comma-separated schemas to exclude
      --clean            Delete output directory before export
      --dry-run          Connect and report what would be exported without writing files
      --parallel         Run metadata queries concurrently (faster on large databases)
      --config           Path to pgschema-export.json
      --include-<kind>   Include an object kind in the export
      --exclude-<kind>   Exclude an object kind from the export
                         kinds: schemas, extensions, types, sequences, domains,
                         foreign-tables, tables, constraints, indexes, views,
                         triggers, event-triggers, rules, aggregates, operators,
                         casts, publications, subscriptions, policies, comments,
                         grants, functions

Diff options:
  -l, --left             Left (baseline) exported schema directory
      --left-db          Left (baseline) live PostgreSQL connection string
  -r, --right            Right (target) exported schema directory
      --right-db         Right (target) live PostgreSQL connection string
  -o, --output           Optional path to write the diff report
      --format           Output format: text (default), json, or html
                         Inferred from the --output extension (.json/.html) if omitted.
                         Exit code 2 indicates differences were found.
      --schemas          Comma-separated schemas to export for live-db diff. Default: public
      --exclude-schemas  Comma-separated schemas to exclude for live-db diff
      --parallel         Run live-db metadata queries concurrently (faster on large databases)
      --ignore-comments  Ignore SQL comments when comparing files
      --ignore-whitespace Ignore whitespace-only differences when comparing files
      --context          Show line-by-line changes within each changed file

Watch options:
  -l, --left             Left (baseline) exported schema directory
  -r, --right            Right (target) exported schema directory
      --format           Console report format: text (default), json, or html

Migrate options:
  -f, --from             Baseline (old) exported schema directory
  -t, --to               Target (new) exported schema directory
  -o, --output           Output directory for generated migrations. Default: ./migrations
  -n, --name             Optional name appended to the generated file names
      --safe             Emit destructive statements (DROP, type changes) as comments
      --preview          Print the migration to stdout without writing files

Split-dump options:
  -i, --input            Input SQL file created by pg_dump
  -o, --output           Output directory
      --clean            Delete output directory before split
      --no-deploy        Do not generate deploy.sql

Recommended pg_dump:
  pg_dump --schema-only --no-owner --no-privileges --file schema.sql mydb

Version:
  --version, -v    Show the installed version
""");
}
