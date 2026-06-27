using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Configuration;
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
    Console.WriteLine("pgschema-export 0.9.0");
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

        await exporter.ExportAsync(options);

        Console.WriteLine("Schema export completed.");
        Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");
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

            default:
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

static string[] Split(string value)
{
    return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static void PrintHelp()
{
    Console.WriteLine("""
PostgreSQL Git-Native Schema Exporter 0.9.0

Usage:
  pgschema-export export --connection "<connection-string>" --output "./db-schema"
  pgschema-export split-dump --input "./schema.sql" --output "./db-schema"

Commands:
  export       Live export from PostgreSQL using pg_catalog/information_schema
  split-dump   Split existing pg_dump schema-only SQL file into folders

Export options:
  -c, --connection       PostgreSQL connection string
  -o, --output           Output directory
      --schemas          Comma-separated schemas. Default: public
      --exclude-schemas  Comma-separated schemas to exclude
      --clean            Delete output directory before export
      --config           Path to pgschema-export.json

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
