using Microsoft.Extensions.Logging;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Metadata;

public interface IMetadataProvider
{
    Task<DatabaseModel> LoadAsync(
        string connectionString,
        ExportOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);
}
