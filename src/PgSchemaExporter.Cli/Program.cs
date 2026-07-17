using Microsoft.Extensions.Logging;
using PgSchemaExporter.Cli;
using PgSchemaExporter.Cli.Commands;
using PgSchemaExporter.Cli.Diagnostics;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Configuration;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Diagramming;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Drift;
using PgSchemaExporter.Core.Integrity;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Hazards;
using PgSchemaExporter.Core.Migration.Plan;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;

const string VersionString = "1.9.0";

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

// Extract global flags before command-specific parsing.
var verbosity = ResolveVerbosity(args);
var profile = Array.IndexOf(args, "--profile") >= 0;
var filtered = new string[args.Length];
var filteredCount = 0;
for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (a != "--verbose" && a != "--quiet" && a != "--profile")
        filtered[filteredCount++] = a;
}
if (filteredCount < args.Length)
    Array.Resize(ref filtered, filteredCount);
args = filtered;

IProgressReporter progress = new ConsoleProgressReporter(verbosity);
ILogger logger = new ConsoleLogger(verbosity);
IMetadataProvider metadataProvider = new PostgresMetadataProvider();

// When profiling, wrap the reporter so per-phase timings can be summarized afterwards.
TimingProgressReporter? timing = null;
if (profile)
{
    timing = new TimingProgressReporter(progress);
    progress = timing;
}

try
{
    var command = args[0];

    var dispatcher = new CommandDispatcher(metadataProvider);
    var dispatched = await dispatcher.ExecuteAsync(command, args[1..], progress, logger);
    if (dispatched.HasValue)
    {
        Environment.ExitCode = dispatched.Value;
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
finally
{
    if (timing is not null)
        Console.Error.WriteLine(timing.BuildSummary());
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
PostgreSQL Git-Native Schema Exporter 1.9.0

Usage:
  pgschema-export init [--output "./pgschema-export.json"]
  pgschema-export export --connection "<connection-string>" --output "./db-schema"
  pgschema-export split-dump --input "./schema.sql" --output "./db-schema"
  pgschema-export diff --left "./old-schema" --right "./new-schema"
  pgschema-export watch --left "./old-schema" --right "./new-schema"
  pgschema-export migrate --from "./old-schema" --to "./new-schema" --output "./migrations"
  pgschema-export drift --schema "./db-schema" --connection "<connection-string>"
  pgschema-export fingerprint --schema "./db-schema" [--output schema.fingerprint.json]
  pgschema-export plan --from "./old-schema" --to "./new-schema" --output plan.json
  pgschema-export apply --plan plan.json --connection "<connection-string>"
  pgschema-export diagram --connection "<connection-string>" --output diagram.mmd
  pgschema-export diagram --schema "./db-schema" --output schema.dot

Commands:
  init         Create a pgschema-export.json config template
  export       Live export from PostgreSQL using pg_catalog/information_schema
  split-dump   Split existing pg_dump schema-only SQL file into folders
  diff         Compare two exported schema directories and report changes
  watch        Continuously re-run a directory diff as files change
  migrate      Generate up/down migration scripts between two exported schemas
  drift        Detect drift between a committed schema directory and a live DB
  fingerprint  Compute (or verify) a SHA256 fingerprint of a schema directory
  plan         Generate a reviewable migration plan (declarative workflow)
  apply        Apply a migration plan to a live database
  diagram      Generate an ER diagram (Mermaid or Graphviz DOT)

Global options:
      --verbose          Print detailed per-object progress and debug logs (to stderr)
      --quiet            Suppress progress output; only errors are shown
      --profile          Print per-phase timing summary to stderr on completion
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
                         A history.json record is appended to the output directory.
      --online-ddl       Rewrite index create/drop to CONCURRENTLY (zero-downtime)
      --lock-timeout     Emit SET lock_timeout guard (e.g. 5s, 30s, 1min)
      --statement-timeout Emit SET statement_timeout guard
      --warn-hazards     Print a hazard analysis of the generated migration

Plan options:
  -f, --from             Baseline (old) exported schema directory
  -t, --to               Target (new) exported schema directory
  -o, --output           Optional path to write the plan JSON file
  -n, --name             Optional plan name
      --format           Output format: human (default) or json
      --safe             Mark destructive statements to be skipped on apply
      --online-ddl       Rewrite index create/drop to CONCURRENTLY
      --lock-timeout     Capture a lock_timeout guard in the plan
      --statement-timeout Capture a statement_timeout guard in the plan
                         Exit code 2 indicates the plan contains pending changes.

Apply options:
  -p, --plan             Path to a plan JSON file produced by 'plan'
  -c, --connection       Target PostgreSQL connection string
      --rollback         Apply the down (rollback) direction instead of up
      --dry-run          Print statements without executing them
  -y, --yes              Skip the interactive confirmation prompt

Drift options:
  -s, --schema           Committed exported schema directory (expected state)
  -c, --connection       Live PostgreSQL connection string (actual state)
  -o, --output           Optional path to write the drift report
      --format           Output format: text (default), json, or html
                         Inferred from the --output extension (.json/.html) if omitted.
                         Exit code 2 indicates drift was detected.
      --schemas          Comma-separated schemas to export from the live DB. Default: public
      --exclude-schemas  Comma-separated schemas to exclude
      --parallel         Run live-db metadata queries concurrently
      --ignore-comments  Ignore SQL comments when comparing
      --ignore-whitespace Ignore whitespace-only differences when comparing
      --context          Show line-by-line changes within each drifted file

Fingerprint options:
  -s, --schema           Exported schema directory to fingerprint
  -o, --output           Optional path to write the fingerprint manifest (JSON)
      --verify           Path to a fingerprint manifest to validate against.
                         Exit code 2 indicates the schema no longer matches.

Diagram options:
  -c, --connection       Live PostgreSQL connection string
  -s, --schema           Exported schema directory (alternative to live DB)
  -o, --output           Optional path to write the diagram file
      --format           Output format: mermaid (default) or dot
                         Inferred from the --output extension (.mmd/.dot) if omitted.
      --schemas          Comma-separated schemas to read from the live DB. Default: public
      --exclude-schemas  Comma-separated schemas to exclude from the live DB

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
