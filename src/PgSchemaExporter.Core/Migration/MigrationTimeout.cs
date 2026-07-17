namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Validates PostgreSQL timeout values (e.g. <c>lock_timeout</c>,
/// <c>statement_timeout</c>) before they are interpolated into SQL. This prevents
/// malformed values and closes the SQL-injection vector that would otherwise exist
/// when a user- or plan-supplied value is embedded in a <c>SET ... = '...'</c>
/// statement.
/// </summary>
public static class MigrationTimeout
{
    // A bare integer is milliseconds; an optional unit may follow (us, ms, s, min, h, d).
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.AsSpan().Trim();
        if (span.IsEmpty)
            return false;

        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
            i++;

        if (i == 0)
            return false;

        while (i < span.Length && char.IsWhiteSpace(span[i]))
            i++;

        if (i == span.Length)
            return true;

        var unit = span[i..];
        if (unit.Equals("us", StringComparison.OrdinalIgnoreCase))
            return true;
        if (unit.Equals("ms", StringComparison.OrdinalIgnoreCase))
            return true;
        if (unit.Equals("s", StringComparison.OrdinalIgnoreCase))
            return true;
        if (unit.Equals("min", StringComparison.OrdinalIgnoreCase))
            return true;
        if (unit.Equals("h", StringComparison.OrdinalIgnoreCase))
            return true;
        if (unit.Equals("d", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

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
