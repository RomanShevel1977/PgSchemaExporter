namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Controls how a <see cref="MigrationScript"/> is rendered to SQL: whether
/// destructive statements are commented out, and optional session-level
/// <c>lock_timeout</c> / <c>statement_timeout</c> guards.
/// </summary>
public sealed class MigrationRenderOptions
{
    /// <summary>When true, destructive statements are emitted as commented-out SQL.</summary>
    public bool Safe { get; init; }

    /// <summary>
    /// Optional PostgreSQL <c>lock_timeout</c> value (e.g. <c>"5s"</c>, <c>"30s"</c>, <c>"1min"</c>).
    /// Emitted as a session-level <c>SET lock_timeout</c> before any statements run.
    /// </summary>
    public string? LockTimeout { get; init; }

    /// <summary>
    /// Optional PostgreSQL <c>statement_timeout</c> value. Emitted as a session-level
    /// <c>SET statement_timeout</c> before any statements run.
    /// </summary>
    public string? StatementTimeout { get; init; }

    public static readonly MigrationRenderOptions Default = new();

    public static MigrationRenderOptions ForSafe(bool safe) => new() { Safe = safe };
}
