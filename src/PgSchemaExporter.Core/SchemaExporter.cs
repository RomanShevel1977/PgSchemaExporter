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
    {
        _metadataProvider = metadataProvider;
        _schemaFileWriter = schemaFileWriter;
        _deployScriptWriter = deployScriptWriter;
        _dependencyManifestWriter = new DependencyManifestWriter();
        _deploymentPlanBuilder = new DeploymentPlanBuilder();
        _readmeWriter = readmeWriter;
    }

    public async Task<ExportSummary> ExportAsync(ExportOptions options, CancellationToken cancellationToken = default)
    {
        Validate(options);

        var model = await _metadataProvider.LoadAsync(options.ConnectionString, options, cancellationToken);

        if (options.DryRun)
        {
            return new ExportSummary
            {
                DryRun = true,
                OutputDirectory = options.OutputDirectory,
                Counts = CountObjects(model)
            };
        }

        if (options.CleanOutputDirectory && Directory.Exists(options.OutputDirectory))
            Directory.Delete(options.OutputDirectory, recursive: true);

        Directory.CreateDirectory(options.OutputDirectory);

        var writeResult = await _schemaFileWriter.WriteAsync(options.OutputDirectory, model, options.Format, cancellationToken);

        var deploymentPlan = _deploymentPlanBuilder.Build(model, writeResult);

        await _deployScriptWriter.WriteAsync(options.OutputDirectory, deploymentPlan.OrderedFiles, cancellationToken);

        await _dependencyManifestWriter.WriteAsync(options.OutputDirectory, deploymentPlan, cancellationToken);

        await _readmeWriter.WriteAsync(options.OutputDirectory, cancellationToken);

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
        ("Policies", model.Policies.Count),
        ("Comments", model.Comments.Count),
        ("Grants", model.Grants.Count)
    ];

    private static void Validate(ExportOptions options)
    {
        options.EnsureValidForExport();
    }
}
