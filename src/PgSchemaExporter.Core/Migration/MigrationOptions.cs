namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Options for generating a migration between two exported schema directories
/// (the git-native layout produced by the exporter or split-dump commands).
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>Baseline (current/old) exported schema directory.</summary>
    public string FromDirectory { get; set; } = "";

    /// <summary>Target (desired/new) exported schema directory.</summary>
    public string ToDirectory { get; set; } = "";

    /// <summary>Directory where the generated migration files are written.</summary>
    public string OutputDirectory { get; set; } = "./migrations";

    /// <summary>Optional human-readable name appended to the generated file names.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// When true, statements that may destroy data (DROP COLUMN, DROP TABLE, type changes)
    /// are emitted as commented-out SQL so they must be reviewed and enabled manually.
    /// </summary>
    public bool Safe { get; set; }

    /// <summary>
    /// When set, the migration is printed to stdout only and no files are written.
    /// </summary>
    public bool Preview { get; set; }

    /// <summary>
    /// When true, index create/drop operations are rewritten to their
    /// <c>CONCURRENTLY</c> forms and emitted outside the transaction block to
    /// avoid taking heavy locks (zero-downtime friendly).
    /// </summary>
    public bool OnlineDdl { get; set; }

    /// <summary>
    /// Optional PostgreSQL <c>lock_timeout</c> (e.g. <c>"5s"</c>) emitted as a
    /// session-level guard before the migration runs.
    /// </summary>
    public string? LockTimeout { get; set; }

    /// <summary>
    /// Optional PostgreSQL <c>statement_timeout</c> emitted as a session-level
    /// guard before the migration runs.
    /// </summary>
    public string? StatementTimeout { get; set; }

    /// <summary>
    /// When true, a hazard analysis of the generated migration is printed,
    /// warning about destructive or lock-heavy operations.
    /// </summary>
    public bool WarnHazards { get; set; }

    /// <summary>Builds the render options that mirror these migration options.</summary>
    public MigrationRenderOptions ToRenderOptions() => new()
    {
        Safe = Safe,
        LockTimeout = LockTimeout,
        StatementTimeout = StatementTimeout
    };

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(FromDirectory))
            throw new ArgumentException("Baseline directory is required (--from).");

        if (string.IsNullOrWhiteSpace(ToDirectory))
            throw new ArgumentException("Target directory is required (--to).");

        if (!Directory.Exists(FromDirectory))
            throw new DirectoryNotFoundException($"Baseline directory was not found: {FromDirectory}");

        if (!Directory.Exists(ToDirectory))
            throw new DirectoryNotFoundException($"Target directory was not found: {ToDirectory}");

        if (!Preview && string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("Output directory is required unless --preview is used.");

        MigrationTimeout.EnsureValid(LockTimeout, "--lock-timeout");
        MigrationTimeout.EnsureValid(StatementTimeout, "--statement-timeout");
    }
}
