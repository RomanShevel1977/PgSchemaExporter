using PgSchemaExporter.Core.Metadata;
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

    public async Task ExportAsync(ExportOptions options, CancellationToken cancellationToken = default)
    {
        Validate(options);

        if (options.CleanOutputDirectory && Directory.Exists(options.OutputDirectory))
            Directory.Delete(options.OutputDirectory, recursive: true);

        Directory.CreateDirectory(options.OutputDirectory);

        var model = await _metadataProvider.LoadAsync(options.ConnectionString, options, cancellationToken);

        var writeResult = await _schemaFileWriter.WriteAsync(options.OutputDirectory, model, cancellationToken);

        var deploymentPlan = _deploymentPlanBuilder.Build(model, writeResult);

        await _deployScriptWriter.WriteAsync(options.OutputDirectory, deploymentPlan.OrderedFiles, cancellationToken);

        await _dependencyManifestWriter.WriteAsync(options.OutputDirectory, deploymentPlan, cancellationToken);

        await _readmeWriter.WriteAsync(options.OutputDirectory, cancellationToken);
    }

    private static void Validate(ExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("Connection string is required.");

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Output directory is required.");

        if (options.Schemas.Length == 0)
            throw new ArgumentException("At least one schema is required.");
    }
}
