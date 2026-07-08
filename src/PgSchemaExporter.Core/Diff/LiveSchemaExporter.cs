using Microsoft.Extensions.Logging;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;

namespace PgSchemaExporter.Core.Diff;

/// <summary>
/// Exports a live PostgreSQL database schema to a temporary directory
/// so it can be compared with another schema (directory or live DB) using
/// the existing directory-based diff logic.
/// </summary>
public sealed class LiveSchemaExporter
{
    public async Task<string> ExportToTempDirectoryAsync(
        string connectionString,
        ExportOptions exportOptions,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pgschema-diff-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempDir);

        var provider = new PostgresMetadataProvider();
        var model = await provider.LoadAsync(connectionString, exportOptions, progress, logger, cancellationToken);

        var writer = new SchemaFileWriter();
        await writer.WriteAsync(tempDir, model, exportOptions.Format, cancellationToken);

        return tempDir;
    }

    public void CleanupTempDirectory(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Log but don't throw - cleanup failures shouldn't mask original errors
        }
    }
}
