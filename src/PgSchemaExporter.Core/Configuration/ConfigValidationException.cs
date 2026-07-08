namespace PgSchemaExporter.Core.Configuration;

/// <summary>
/// Thrown when a configuration file is syntactically valid JSON but contains
/// values that cannot be used for an export. Carries every problem found so the
/// user can fix them in one pass instead of one at a time.
/// </summary>
public sealed class ConfigValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigValidationException(string configPath, IReadOnlyList<string> errors)
        : base(BuildMessage(configPath, errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(string configPath, IReadOnlyList<string> errors)
    {
        var lines = new List<string>
        {
            $"Configuration file has {errors.Count} problem(s): {configPath}"
        };

        foreach (var error in errors)
            lines.Add($"  - {error}");

        return string.Join(Environment.NewLine, lines);
    }
}
