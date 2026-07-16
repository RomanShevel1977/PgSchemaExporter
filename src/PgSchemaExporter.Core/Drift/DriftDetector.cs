using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Diff;
using PgSchemaExporter.Core.Metadata;

namespace PgSchemaExporter.Core.Drift;

/// <summary>
/// Detects whether a live PostgreSQL database has drifted away from the schema
/// state committed to an exported schema directory. Built on top of
/// <see cref="SchemaDiffer"/>: objects only in the committed schema are reported
/// as "missing" (removed from the live DB) and objects only in the live database
/// are reported as "unexpected" (added out-of-band).
/// </summary>
public sealed class DriftDetector
{
    private readonly SchemaDiffer _differ;

    public DriftDetector(IMetadataProvider? metadataProvider = null)
    {
        _differ = new SchemaDiffer(metadataProvider);
    }

    public async Task<DriftResult> DetectAsync(
        DriftOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= NullProgressReporter.Instance;
        logger ??= NullLogger.Instance;

        options.EnsureValid();

        progress.Step("Detecting schema drift");
        var diff = await _differ.DiffAsync(options.ToDiffOptions(), progress, logger, cancellationToken);

        return new DriftResult
        {
            // Left = committed schema, Right = live DB.
            // Objects "added" in the live DB are unexpected; "removed" are missing.
            Unexpected = diff.Added,
            Missing = diff.Removed,
            Modified = diff.Changed,
            Diff = diff
        };
    }
}

/// <summary>
/// The outcome of a drift detection run, framed in terms of the live database's
/// deviation from the committed schema.
/// </summary>
public sealed class DriftResult
{
    /// <summary>Objects present in the live database but not in the committed schema.</summary>
    public IReadOnlyList<string> Unexpected { get; init; } = [];

    /// <summary>Objects present in the committed schema but missing from the live database.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>Objects that exist in both but whose definitions differ.</summary>
    public IReadOnlyList<string> Modified { get; init; } = [];

    /// <summary>The underlying schema diff (committed schema vs live database).</summary>
    public SchemaDiffResult Diff { get; init; } = new();

    public bool HasDrifted => Unexpected.Count > 0 || Missing.Count > 0 || Modified.Count > 0;
}
