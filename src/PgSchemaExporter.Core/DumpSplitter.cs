using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core;

public sealed class DumpSplitter
{
    private readonly SqlStatementSplitter _statementSplitter;
    private readonly PgDumpObjectClassifier _classifier;
    private readonly DumpSplitFileWriter _fileWriter;
    private readonly DeployScriptWriter _deployScriptWriter;
    private readonly DependencyManifestWriter _dependencyManifestWriter;
    private readonly DeploymentPlanBuilder _deploymentPlanBuilder;
    private readonly ReadmeWriter _readmeWriter;

    public DumpSplitter(
        SqlStatementSplitter statementSplitter,
        PgDumpObjectClassifier classifier,
        DumpSplitFileWriter fileWriter,
        DeployScriptWriter deployScriptWriter,
        ReadmeWriter readmeWriter)
    {
        _statementSplitter = statementSplitter;
        _classifier = classifier;
        _fileWriter = fileWriter;
        _deployScriptWriter = deployScriptWriter;
        _dependencyManifestWriter = new DependencyManifestWriter();
        _deploymentPlanBuilder = new DeploymentPlanBuilder();
        _readmeWriter = readmeWriter;
    }

    public async Task SplitAsync(SplitDumpOptions options, CancellationToken cancellationToken = default)
    {
        Validate(options);

        if (options.CleanOutputDirectory && Directory.Exists(options.OutputDirectory))
            Directory.Delete(options.OutputDirectory, recursive: true);

        Directory.CreateDirectory(options.OutputDirectory);

        var sql = await File.ReadAllTextAsync(options.InputFile, cancellationToken);
        var statements = _statementSplitter.Split(sql);

        var objects = statements
            .Select((statement, index) => _classifier.Classify(statement, index + 1))
            .Where(x => !string.IsNullOrWhiteSpace(x.Statement))
            .ToList();

        var writeResult = await _fileWriter.WriteAsync(options.OutputDirectory, objects, cancellationToken);

        var deploymentPlan = _deploymentPlanBuilder.Build(writeResult);

        if (options.GenerateDeployScript)
            await _deployScriptWriter.WriteAsync(options.OutputDirectory, deploymentPlan.OrderedFiles, cancellationToken);

        await _dependencyManifestWriter.WriteAsync(options.OutputDirectory, deploymentPlan, cancellationToken);

        await _readmeWriter.WriteAsync(options.OutputDirectory, cancellationToken);
        await WriteSplitReportAsync(options.OutputDirectory, objects, cancellationToken);
    }

    private static void Validate(SplitDumpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputFile))
            throw new ArgumentException("Input file is required.");

        if (!File.Exists(options.InputFile))
            throw new FileNotFoundException("Input SQL dump was not found.", options.InputFile);

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Output directory is required.");
    }

    private static async Task WriteSplitReportAsync(
        string outputDirectory,
        IReadOnlyList<SqlDumpObject> objects,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            "# Split dump report",
            "",
            $"Total statements: {objects.Count}",
            ""
        };

        foreach (var group in objects.GroupBy(x => x.Type).OrderBy(x => x.Key.ToString()))
            lines.Add($"- {group.Key}: {group.Count()}");

        lines.Add("");
        lines.Add("## Notes");
        lines.Add("");
        lines.Add("This mode is intended for `pg_dump --schema-only --no-owner --no-privileges` output.");
        lines.Add("Complex dumps with data, COPY blocks, ownership, grants, or custom SQL may require manual review.");

        await File.WriteAllLinesAsync(Path.Combine(outputDirectory, "split-report.md"), lines, cancellationToken);
    }
}
