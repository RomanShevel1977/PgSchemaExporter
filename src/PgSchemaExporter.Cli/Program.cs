using Microsoft.Extensions.Logging;
using PgSchemaExporter.Cli;
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
var profile = args.Contains("--profile");
args = args.Where(a => a is not "--verbose" and not "--quiet" and not "--profile").ToArray();

IProgressReporter progress = new ConsoleProgressReporter(verbosity);
ILogger logger = new ConsoleLogger(verbosity);

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

        if (options.WarnHazards)
            PrintHazards(HazardAnalyzer.Analyze(script)
                .Select(h => (h.Severity.ToString(), h.Category.ToString(), h.Message, h.Statement)));

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
            var result = await writer.WriteAsync(options, script, timestamp);

            await MigrationHistory.AppendAsync(options.OutputDirectory, new MigrationHistoryEntry
            {
                AppliedAt = timestamp,
                Name = options.Name,
                UpFile = Path.GetFileName(result.UpFile),
                DownFile = Path.GetFileName(result.DownFile),
                UpStatements = script.Up.Count,
                DownStatements = script.Down.Count,
                Destructive = script.HasDestructiveChanges
            });

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

    if (string.Equals(command, "drift", StringComparison.OrdinalIgnoreCase))
    {
        var options = ParseDriftOptions(args.Skip(1).ToArray());

        var detector = new DriftDetector();
        var result = await detector.DetectAsync(options, progress, logger);

        var reportWriter = new SchemaDiffReportWriter();

        // The diff report goes to stdout (may be JSON/HTML for machine consumption);
        // the human-readable drift summary goes to stderr so it never corrupts
        // structured stdout output (e.g. `drift --format json | jq`).
        Console.WriteLine(reportWriter.BuildReport(result.Diff, options.Format));

        if (result.HasDrifted)
        {
            Console.Error.WriteLine("Drift detected between the committed schema and the live database:");
            Console.Error.WriteLine($"  Missing from live DB:   {result.Missing.Count}");
            Console.Error.WriteLine($"  Unexpected in live DB:  {result.Unexpected.Count}");
            Console.Error.WriteLine($"  Modified definitions:   {result.Modified.Count}");
        }
        else
        {
            Console.Error.WriteLine("No drift detected. The live database matches the committed schema.");
        }

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            await reportWriter.WriteAsync(options.OutputFile, result.Diff, options.Format);
            Console.Error.WriteLine($"Report written to: {Path.GetFullPath(options.OutputFile)}");
        }

        Environment.ExitCode = result.HasDrifted ? 2 : 0;
        return;
    }

    if (string.Equals(command, "fingerprint", StringComparison.OrdinalIgnoreCase))
    {
        var (schema, output, verify) = ParseFingerprintOptions(args.Skip(1).ToArray());

        var result = SchemaFingerprint.Compute(schema);

        if (!string.IsNullOrWhiteSpace(verify))
        {
            var expected = await SchemaFingerprintFile.ReadAsync(verify);
            if (string.Equals(expected.Fingerprint, result.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Fingerprint OK. The schema matches the stored fingerprint.");
                Console.WriteLine($"  Fingerprint: {result.Fingerprint}");
                Environment.ExitCode = 0;
            }
            else
            {
                Console.Error.WriteLine("Fingerprint MISMATCH. The schema has changed since the fingerprint was generated.");
                Console.Error.WriteLine($"  Expected: {expected.Fingerprint}");
                Console.Error.WriteLine($"  Actual:   {result.Fingerprint}");

                var comparison = SchemaFingerprint.CompareFiles(expected.Files, result);
                if (comparison.HasDifferences)
                {
                    foreach (var path in comparison.Added)
                        Console.Error.WriteLine($"  + added:    {path}");
                    foreach (var path in comparison.Removed)
                        Console.Error.WriteLine($"  - removed:  {path}");
                    foreach (var path in comparison.Modified)
                        Console.Error.WriteLine($"  ~ modified: {path}");
                }

                Environment.ExitCode = 2;
            }
            return;
        }

        Console.WriteLine($"Fingerprint: {result.Fingerprint}");
        Console.WriteLine($"Files:       {result.FileCount}");

        if (!string.IsNullOrWhiteSpace(output))
        {
            await SchemaFingerprintFile.WriteAsync(output, result);
            Console.WriteLine($"Written to: {Path.GetFullPath(output)}");
        }

        return;
    }

    if (string.Equals(command, "plan", StringComparison.OrdinalIgnoreCase))
    {
        var (migrateOptions, planFile, format) = CliParser.ParsePlanOptions(args.Skip(1).ToArray());

        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(migrateOptions);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(MigrationPlanFile.Serialize(plan));
        else
            Console.WriteLine(MigrationPlanRenderer.RenderHuman(plan));

        if (!string.IsNullOrWhiteSpace(planFile))
        {
            await MigrationPlanFile.WriteAsync(planFile, plan);
            Console.WriteLine($"Plan written to: {Path.GetFullPath(planFile)}");
        }

        // Exit code 2 signals there are pending changes (useful for CI gating).
        Environment.ExitCode = plan.HasChanges ? 2 : 0;
        return;
    }

    if (string.Equals(command, "apply", StringComparison.OrdinalIgnoreCase))
    {
        var applyArgs = CliParser.ParseApplyOptions(args.Skip(1).ToArray());

        var plan = await MigrationPlanFile.ReadAsync(applyArgs.PlanFile);

        if (!plan.HasChanges)
        {
            Console.WriteLine("Plan contains no changes. Nothing to apply.");
            return;
        }

        PrintHazards(plan.Hazards
            .Select(h => (h.Severity, h.Category, h.Message, h.Statement)));

        if (!applyArgs.DryRun && !applyArgs.AssumeYes)
        {
            Console.Write($"Apply {(applyArgs.Rollback ? "rollback (down)" : "up")} migration to the target database? [y/N] ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                Environment.ExitCode = 1;
                return;
            }
        }

        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = applyArgs.ConnectionString,
            Rollback = applyArgs.Rollback,
            DryRun = applyArgs.DryRun
        }, progress, logger);

        if (result.DryRun)
            Console.WriteLine($"Dry run complete. {result.Skipped} destructive statement(s) would be skipped (safe plan).");
        else
            Console.WriteLine($"Applied {result.Executed} statement(s). Skipped {result.Skipped}.");

        return;
    }

    if (string.Equals(command, "diagram", StringComparison.OrdinalIgnoreCase))
    {
        var options = CliParser.ParseDiagramOptions(args.Skip(1).ToArray());

        var generator = new SchemaDiagramGenerator();
        var diagram = await generator.GenerateAsync(options, progress, logger);

        if (!string.IsNullOrWhiteSpace(options.OutputFile))
        {
            await File.WriteAllTextAsync(options.OutputFile, diagram);
            Console.WriteLine($"Diagram written to: {Path.GetFullPath(options.OutputFile)}");
        }
        else
        {
            Console.WriteLine(diagram);
        }

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

if (timing is not null)
    Console.Error.WriteLine(timing.BuildSummary());

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

static SchemaDiffOptions ParseDiffOptions(string[] args)
{
    return CliParser.ParseDiffOptions(args);
}

static DriftOptions ParseDriftOptions(string[] args)
{
    return CliParser.ParseDriftOptions(args);
}

static (string Schema, string? Output, string? Verify) ParseFingerprintOptions(string[] args)
{
    return CliParser.ParseFingerprintOptions(args);
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

static void PrintHazards(IEnumerable<(string Severity, string Category, string Message, string Statement)> hazards)
{
    var list = hazards.ToList();
    if (list.Count == 0)
        return;

    static int Rank(string severity) => severity.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    Console.WriteLine($"Hazards detected ({list.Count}):");
    foreach (var hazard in list.OrderByDescending(h => Rank(h.Severity)))
    {
        Console.WriteLine($"  [{hazard.Severity.ToUpperInvariant()}] {hazard.Category}: {hazard.Message}");
        Console.WriteLine($"      {hazard.Statement}");
    }
    Console.WriteLine();
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
