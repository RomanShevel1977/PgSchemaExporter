using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Metadata;

public interface IMetadataProvider
{
    Task<DatabaseModel> LoadAsync(
        string connectionString,
        ExportOptions options,
        CancellationToken cancellationToken = default);
}
