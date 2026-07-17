using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;

namespace PgSchemaExporter.Core;

public sealed class SchemaExporter
{
    private readonly IMetadataProvider _metadataProvider;
    private readonly SchemaFileWriter _schemaFileWriter;
    private readonly DeployScriptWriter _deployScriptWriter;
    private readonly DependencyManifestWriter _dependencyManifestWriter;
    private readonly DeploymentPlanBuilder _deploymentPlanBuilder;
    private readonly ReadmeWriter _readmeWriter;

    public SchemaExporter(
        IMetadataProvider metadataProvider,
        SchemaFileWriter schemaFileWriter,
        DeployScriptWriter deployScriptWriter,
        ReadmeWriter readmeWriter)
        : this(metadataProvider, schemaFileWriter, deployScriptWriter, readmeWriter, new DependencyManifestWriter(), new DeploymentPlanBuilder())
    {
    }

    public SchemaExporter(
        IMetadataProvider metadataProvider,
        SchemaFileWriter schemaFileWriter,
        DeployScriptWriter deployScriptWriter,
        ReadmeWriter readmeWriter,
        DependencyManifestWriter dependencyManifestWriter,
        DeploymentPlanBuilder deploymentPlanBuilder)
    {
        _metadataProvider = metadataProvider;
        _schemaFileWriter = schemaFileWriter;
        _deployScriptWriter = deployScriptWriter;
        _dependencyManifestWriter = dependencyManifestWriter;
        _deploymentPlanBuilder = deploymentPlanBuilder;
        _readmeWriter = readmeWriter;
    }

    public async Task<ExportSummary> ExportAsync(
        ExportOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= NullProgressReporter.Instance;
        logger ??= NullLogger.Instance;

        Validate(options);

        var model = await _metadataProvider.LoadAsync(
            options.ConnectionString, options, progress, logger, cancellationToken);

        if (options.DryRun)
        {
            logger.LogInformation("Dry run: discovered {Count} objects", CountObjects(model).Sum(c => c.Count));
            return new ExportSummary
            {
                DryRun = true,
                OutputDirectory = options.OutputDirectory,
                Counts = CountObjects(model)
            };
        }

        if (options.CleanOutputDirectory && Directory.Exists(options.OutputDirectory))
        {
            progress.Step("Cleaning output directory");
            Directory.Delete(options.OutputDirectory, recursive: true);
        }

        Directory.CreateDirectory(options.OutputDirectory);

        progress.Step("Writing schema files");
        var writeResult = await _schemaFileWriter.WriteAsync(options.OutputDirectory, model, options.Format, cancellationToken);

        progress.Step("Building deployment plan");
        var deploymentPlan = _deploymentPlanBuilder.Build(model, writeResult);

        progress.Step("Writing deploy script");
        await _deployScriptWriter.WriteAsync(options.OutputDirectory, deploymentPlan.OrderedFiles, cancellationToken);

        await _dependencyManifestWriter.WriteAsync(options.OutputDirectory, deploymentPlan, cancellationToken);

        await _readmeWriter.WriteAsync(options.OutputDirectory, cancellationToken);

        progress.Complete("Export finished");
        return new ExportSummary
        {
            DryRun = false,
            OutputDirectory = options.OutputDirectory,
            Counts = CountObjects(model)
        };
    }

    private static IReadOnlyList<(string ObjectKind, int Count)> CountObjects(DatabaseModel model) =>
    [
        ("Schemas", model.Schemas.Count),
        ("Extensions", model.Extensions.Count),
        ("Types", model.Types.Count),
        ("Sequences", model.Sequences.Count),
        ("Domains", model.Domains.Count),
        ("Foreign tables", model.ForeignTables.Count),
        ("Tables", model.Tables.Count),
        ("Constraints", model.Constraints.Count),
        ("Indexes", model.Indexes.Count),
        ("Views", model.Views.Count),
        ("Functions", model.Functions.Count),
        ("Triggers", model.Triggers.Count),
        ("Event triggers", model.EventTriggers.Count),
        ("Rules", model.Rules.Count),
        ("Aggregates", model.Aggregates.Count),
        ("Operators", model.Operators.Count),
        ("Casts", model.Casts.Count),
        ("Publications", model.Publications.Count),
        ("Subscriptions", model.Subscriptions.Count),
        ("Policies", model.Policies.Count),
        ("Comments", model.Comments.Count),
        ("Grants", model.Grants.Count)
    ];

    private static void Validate(ExportOptions options)
    {
        options.EnsureValidForExport();
    }
}
