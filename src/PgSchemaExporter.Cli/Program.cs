using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Configuration;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return;
}

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.WriteLine("pgschema-export 1.0.0");
    return;
}

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

        var summary = await exporter.ExportAsync(options);

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
        var result = differ.Diff(options);

        var reportWriter = new SchemaDiffReportWriter();
        Console.WriteLine(reportWriter.BuildReport(result));

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            await reportWriter.WriteAsync(options.OutputFile, result);
            Console.WriteLine($"Report written to: {Path.GetFullPath(options.OutputFile)}");
        }

        Environment.ExitCode = result.HasDifferences ? 2 : 0;
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

    Console.Error.WriteLine($"Unknown command: {command}");
    PrintHelp();
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Operation failed:");
    Console.Error.WriteLine(ex.Message);
    if (ex.InnerException is not null)
        Console.Error.WriteLine($"Inner error: {ex.InnerException.Message}");
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

static SchemaDiffOptions ParseDiffOptions(string[] args)
{
    var options = new SchemaDiffOptions();

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
            case "--left":
            case "-l":
                options.LeftDirectory = NextValue();
                break;

            case "--right":
            case "-r":
                options.RightDirectory = NextValue();
                break;

            case "--output":
            case "-o":
                options.OutputFile = NextValue();
                break;

            default:
                throw new ArgumentException($"Unknown argument: {arg}");
        }
    }

    return options;
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

static void PrintHelp()
{
    Console.WriteLine("""
PostgreSQL Git-Native Schema Exporter 1.0.0

Usage:
  pgschema-export export --connection "<connection-string>" --output "./db-schema"
  pgschema-export split-dump --input "./schema.sql" --output "./db-schema"
  pgschema-export diff --left "./old-schema" --right "./new-schema"

Commands:
  export       Live export from PostgreSQL using pg_catalog/information_schema
  split-dump   Split existing pg_dump schema-only SQL file into folders
  diff         Compare two exported schema directories and report changes

Export options:
  -c, --connection       PostgreSQL connection string
  -o, --output           Output directory
      --schemas          Comma-separated schemas. Default: public
      --exclude-schemas  Comma-separated schemas to exclude
      --clean            Delete output directory before export
      --dry-run          Connect and report what would be exported without writing files
      --config           Path to pgschema-export.json
      --include-<kind>   Include an object kind in the export
      --exclude-<kind>   Exclude an object kind from the export
                         kinds: schemas, extensions, types, sequences, domains,
                         foreign-tables, tables, constraints, indexes, views,
                         triggers, policies, comments, grants, functions

Diff options:
  -l, --left             Left (baseline) exported schema directory
  -r, --right            Right (target) exported schema directory
  -o, --output           Optional path to write the diff report
                         Exit code 2 indicates differences were found.

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
