namespace PgSchemaExporter.Cli;

using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Drift;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Options;

public sealed class ApplyArgs
{
    public string PlanFile { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public bool Rollback { get; set; }
    public bool DryRun { get; set; }
    public bool AssumeYes { get; set; }
}

public static class CliParser
{
    /// <summary>
    /// Parses <c>plan</c> arguments into migration options (with <c>Preview</c> set
    /// so no output directory is required), an optional plan file path, and the
    /// output format (<c>human</c> or <c>json</c>).
    /// </summary>
    public static (MigrationOptions Options, string? PlanFile, string Format) ParsePlanOptions(string[] args)
    {
        var options = new MigrationOptions { Preview = true };
        string? planFile = null;
        var format = "human";

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
                    planFile = NextValue();
                    break;

                case "--name":
                case "-n":
                    options.Name = NextValue();
                    break;

                case "--format":
                    format = NextValue().ToLowerInvariant();
                    if (format is not ("human" or "json"))
                        throw new ArgumentException($"Unknown format: {format}. Use 'human' or 'json'.");
                    break;

                case "--safe":
                    options.Safe = true;
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

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return (options, planFile, format);
    }

    public static ApplyArgs ParseApplyOptions(string[] args)
    {
        var result = new ApplyArgs();

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
                case "--plan":
                case "-p":
                    result.PlanFile = NextValue();
                    break;

                case "--connection":
                case "-c":
                    result.ConnectionString = NextValue();
                    break;

                case "--rollback":
                    result.Rollback = true;
                    break;

                case "--dry-run":
                    result.DryRun = true;
                    break;

                case "--yes":
                case "-y":
                    result.AssumeYes = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(result.PlanFile))
            throw new ArgumentException("Plan file is required (--plan).");

        if (!result.DryRun && string.IsNullOrWhiteSpace(result.ConnectionString))
            throw new ArgumentException("Connection string is required unless --dry-run is used (--connection).");

        return result;
    }

    public static DriftOptions ParseDriftOptions(string[] args)
    {
        var options = new DriftOptions();
        var formatExplicit = false;

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
                case "--schema":
                case "-s":
                    options.SchemaDirectory = NextValue();
                    break;

                case "--connection":
                case "-c":
                    options.ConnectionString = NextValue();
                    break;

                case "--output":
                case "-o":
                    options.OutputFile = NextValue();
                    break;

                case "--format":
                    var format = NextValue();
                    options.Format = format.ToLowerInvariant() switch
                    {
                        "json" => DiffFormat.Json,
                        "text" => DiffFormat.Text,
                        "html" => DiffFormat.Html,
                        _ => throw new ArgumentException($"Unknown format: {format}. Use 'text', 'json', or 'html'.")
                    };
                    formatExplicit = true;
                    break;

                case "--schemas":
                    var schemas = Split(NextValue());
                    if (schemas.Length == 0)
                        throw new ArgumentException("--schemas cannot be empty");
                    options.Schemas = schemas;
                    break;

                case "--exclude-schemas":
                    var excludeSchemas = Split(NextValue());
                    if (excludeSchemas.Length == 0)
                        throw new ArgumentException("--exclude-schemas cannot be empty");
                    options.ExcludeSchemas = excludeSchemas;
                    break;

                case "--parallel":
                    options.Parallel = true;
                    break;

                case "--ignore-comments":
                    options.IgnoreComments = true;
                    break;

                case "--ignore-whitespace":
                    options.IgnoreWhitespace = true;
                    break;

                case "--context":
                    options.ShowContext = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (!formatExplicit && !string.IsNullOrWhiteSpace(options.OutputFile))
        {
            options.Format = Path.GetExtension(options.OutputFile).ToLowerInvariant() switch
            {
                ".html" or ".htm" => DiffFormat.Html,
                ".json" => DiffFormat.Json,
                _ => options.Format
            };
        }

        return options;
    }

    public static (string Schema, string? Output, string? Verify) ParseFingerprintOptions(string[] args)
    {
        var schema = "";
        string? output = null;
        string? verify = null;

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
                case "--schema":
                case "-s":
                    schema = NextValue();
                    break;

                case "--output":
                case "-o":
                    output = NextValue();
                    break;

                case "--verify":
                    verify = NextValue();
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(schema))
            throw new ArgumentException("Schema directory is required (--schema).");

        return (schema, output, verify);
    }

    public static SchemaDiffOptions ParseDiffOptions(string[] args)
    {
        var options = new SchemaDiffOptions();
        var formatExplicit = false;

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

                case "--left-db":
                    options.LeftConnectionString = NextValue();
                    break;

                case "--right":
                case "-r":
                    options.RightDirectory = NextValue();
                    break;

                case "--right-db":
                    options.RightConnectionString = NextValue();
                    break;

                case "--output":
                case "-o":
                    options.OutputFile = NextValue();
                    break;

                case "--format":
                    var format = NextValue();
                    options.Format = format.ToLowerInvariant() switch
                    {
                        "json" => DiffFormat.Json,
                        "text" => DiffFormat.Text,
                        "html" => DiffFormat.Html,
                        _ => throw new ArgumentException($"Unknown format: {format}. Use 'text', 'json', or 'html'.")
                    };
                    formatExplicit = true;
                    break;

                case "--schemas":
                    var schemas = Split(NextValue());
                    if (schemas.Length == 0)
                        throw new ArgumentException("--schemas cannot be empty");
                    options.Schemas = schemas;
                    break;

                case "--exclude-schemas":
                    var excludeSchemas = Split(NextValue());
                    if (excludeSchemas.Length == 0)
                        throw new ArgumentException("--exclude-schemas cannot be empty");
                    options.ExcludeSchemas = excludeSchemas;
                    break;

                case "--parallel":
                    options.Parallel = true;
                    break;

                case "--ignore-comments":
                    options.IgnoreComments = true;
                    break;

                case "--ignore-whitespace":
                    options.IgnoreWhitespace = true;
                    break;

                case "--context":
                    options.ShowContext = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        // Infer HTML/JSON format from the output file extension when not set explicitly.
        if (!formatExplicit && !string.IsNullOrWhiteSpace(options.OutputFile))
        {
            options.Format = Path.GetExtension(options.OutputFile).ToLowerInvariant() switch
            {
                ".html" or ".htm" => DiffFormat.Html,
                ".json" => DiffFormat.Json,
                _ => options.Format
            };
        }

        return options;
    }

    private static string[] Split(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
