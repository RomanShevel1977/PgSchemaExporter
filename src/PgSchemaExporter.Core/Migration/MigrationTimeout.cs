using System.Text.RegularExpressions;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Validates PostgreSQL timeout values (e.g. <c>lock_timeout</c>,
/// <c>statement_timeout</c>) before they are interpolated into SQL. This prevents
/// malformed values and closes the SQL-injection vector that would otherwise exist
/// when a user- or plan-supplied value is embedded in a <c>SET ... = '...'</c>
/// statement.
/// </summary>
public static partial class MigrationTimeout
{
    // A bare integer is milliseconds; an optional unit may follow (us, ms, s, min, h, d).
    [GeneratedRegex(@"^\d+\s*(us|ms|s|min|h|d)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeoutRegex();

    public static bool IsValid(string value)
        => !string.IsNullOrWhiteSpace(value) && TimeoutRegex().IsMatch(value.Trim());

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="value"/> is set
    /// but is not a valid PostgreSQL timeout literal. Null/empty is treated as
    /// "not configured" and passes validation.
    /// </summary>
    public static void EnsureValid(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!IsValid(value))
            throw new ArgumentException(
                $"Invalid timeout value for {parameterName}: '{value}'. " +
                "Use a number of milliseconds or a value with a unit, e.g. '5s', '30s', '1min', '500ms'.",
                parameterName);
    }
}
