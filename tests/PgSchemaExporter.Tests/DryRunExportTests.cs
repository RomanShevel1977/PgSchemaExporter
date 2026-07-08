using Microsoft.Extensions.Logging;
using PgSchemaExporter.Core;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DryRunExportTests : IDisposable
{
    private readonly string _output;

    public DryRunExportTests()
    {
        _output = Path.Combine(Path.GetTempPath(), "pgschema-dryrun-" + Guid.NewGuid().ToString("n"));
    }

    private sealed class StubMetadataProvider : IMetadataProvider
    {
        public Task<DatabaseModel> LoadAsync(string connectionString, ExportOptions options, IProgressReporter? progress = null, ILogger? logger = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DatabaseModel
            {
                Tables = [new DbTable { Schema = "public", Name = "users" }],
                Views = [new DbView { Schema = "public", Name = "v", Definition = "SELECT 1" }]
            });
    }

    private static SchemaExporter CreateExporter() => new(
        new StubMetadataProvider(),
        new SchemaFileWriter(),
        new DeployScriptWriter(),
        new ReadmeWriter());

    [Fact]
    public async Task DryRun_DoesNotCreateOutputDirectory_AndReportsCounts()
    {
        var options = new ExportOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
            OutputDirectory = _output,
            DryRun = true
        };

        var summary = await CreateExporter().ExportAsync(options);

        Assert.True(summary.DryRun);
        Assert.False(Directory.Exists(_output));
        Assert.Equal(2, summary.TotalObjects);
        Assert.Contains(summary.Counts, c => c.ObjectKind == "Tables" && c.Count == 1);
        Assert.Contains(summary.Counts, c => c.ObjectKind == "Views" && c.Count == 1);
    }

    [Fact]
    public async Task NormalRun_WritesFiles()
    {
        var options = new ExportOptions
        {
            ConnectionString = "Host=localhost;Database=x;Username=u;Password=p",
            OutputDirectory = _output,
            DryRun = false
        };

        var summary = await CreateExporter().ExportAsync(options);

        Assert.False(summary.DryRun);
        Assert.True(Directory.Exists(_output));
        Assert.True(File.Exists(Path.Combine(_output, "tables", "public.users.sql")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_output))
            Directory.Delete(_output, recursive: true);
    }
}
