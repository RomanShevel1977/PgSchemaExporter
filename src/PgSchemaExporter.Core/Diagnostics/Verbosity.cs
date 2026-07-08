namespace PgSchemaExporter.Core.Diagnostics;

/// <summary>
/// Controls how much information is surfaced to the user on the console.
/// </summary>
public enum Verbosity
{
    /// <summary>Only errors are written. Suppresses progress and summary output.</summary>
    Quiet,

    /// <summary>Default level: high-level progress and summaries.</summary>
    Normal,

    /// <summary>Everything, including per-object-kind timings and debug details.</summary>
    Verbose
}
