namespace PgSchemaExporter.Cli;

using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Options;

public static class CliParser
{
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
