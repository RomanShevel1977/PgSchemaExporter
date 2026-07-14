using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Diagramming;

/// <summary>
/// Orchestrates ER-diagram generation: builds an <see cref="ErModel"/> from a live
/// database or an exported directory, then renders it to the requested format.
/// </summary>
public sealed class SchemaDiagramGenerator
{
    private readonly IMetadataProvider _metadataProvider;

    public SchemaDiagramGenerator(IMetadataProvider? metadataProvider = null)
    {
        _metadataProvider = metadataProvider ?? new PostgresMetadataProvider();
    }

    public async Task<string> GenerateAsync(
        DiagramOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= NullProgressReporter.Instance;
        logger ??= NullLogger.Instance;

        options.EnsureValid();

        ErModel model;
        if (options.UsesLiveDatabase)
        {
            progress.Step("Reading live database schema");
            var dbModel = await _metadataProvider.LoadAsync(
                options.ConnectionString!,
                BuildExportOptions(options),
                progress,
                logger,
                cancellationToken);

            model = ErModelBuilder.FromDatabaseModel(dbModel);
        }
        else
        {
            progress.Step("Reading exported schema directory");
            model = ErModelBuilder.FromDirectory(options.SchemaDirectory!);
        }

        progress.Step($"Building diagram ({model.Tables.Count} tables, {model.Relationships.Count} relationships)");
        return Render(model, options.Format);
    }

    public static string Render(ErModel model, DiagramFormat format) => format switch
    {
        DiagramFormat.Mermaid => MermaidErRenderer.Render(model),
        DiagramFormat.Dot => DotErRenderer.Render(model),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported diagram format.")
    };

    /// <summary>
    /// Builds a minimal <see cref="ExportOptions"/> that loads only what an ER
    /// diagram needs (tables, their columns, and constraints), which also keeps
    /// live reads fast.
    /// </summary>
    private static ExportOptions BuildExportOptions(DiagramOptions options)
    {
        return new ExportOptions
        {
            ConnectionString = options.ConnectionString!,
            OutputDirectory = "./diagram-tmp",
            Schemas = options.Schemas is { Length: > 0 } ? options.Schemas : ["public"],
            ExcludeSchemas = options.ExcludeSchemas ?? ["pg_catalog", "information_schema"],
            Include = new IncludeOptions
            {
                Schemas = false,
                Extensions = false,
                Types = false,
                Sequences = false,
                Domains = false,
                ForeignTables = false,
                Tables = true,
                Constraints = true,
                Indexes = false,
                Views = false,
                Triggers = false,
                EventTriggers = false,
                Rules = false,
                Aggregates = false,
                Operators = false,
                Casts = false,
                Publications = false,
                Subscriptions = false,
                Policies = false,
                Comments = false,
                Grants = false,
                Functions = false
            }
        };
    }
}
