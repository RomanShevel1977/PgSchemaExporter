using BenchmarkDotNet.Attributes;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Output;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Benchmarks;

[MemoryDiagnoser]
public class DeploymentPlanBuilderBenchmark
{
    private readonly DeploymentPlanBuilder _builder = new();
    private DatabaseModel _model = new();
    private FileWriteResult _files = new();

    [GlobalSetup]
    public void Setup()
    {
        const int count = 1_000;

        var schemas = new List<DbSchema>(count / 100);
        for (var s = 0; s < count / 100; s++)
            schemas.Add(new DbSchema { Name = $"schema{s}" });

        var tables = new List<DbTable>(count);
        var tableFiles = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var schema = $"schema{i % (count / 100)}";
            var name = $"t{i}";
            tables.Add(new DbTable
            {
                Schema = schema,
                Name = name,
                Columns =
                [
                    new DbColumn { Name = "id", DataType = "integer" },
                    new DbColumn { Name = "ref", DataType = $"schema{(i + 1) % (count / 100)}.othertype" }
                ]
            });
            tableFiles.Add($"tables/{SqlIdentifier.SafeFileName(schema)}.{SqlIdentifier.SafeFileName(name)}.sql");
        }

        var types = new List<DbType>(count / 100);
        var typeFiles = new List<string>(count / 100);
        for (var i = 0; i < count / 100; i++)
        {
            types.Add(new DbType { Schema = $"schema{i}", Name = "othertype" });
            typeFiles.Add($"types/{SqlIdentifier.SafeFileName($"schema{i}")}.othertype.sql");
        }

        var sequences = new List<DbSequence>(count / 100);
        for (var i = 0; i < count / 100; i++)
            sequences.Add(new DbSequence { Schema = $"schema{i}", Name = $"seq{i}" });

        var constraints = new List<DbConstraint>(count);
        for (var i = 0; i < count; i++)
        {
            var schema = $"schema{i % (count / 100)}";
            var name = $"t{i}";
            constraints.Add(new DbConstraint
            {
                Schema = schema,
                TableName = name,
                Name = $"fk_{i}",
                Type = "f",
                Definition = $"FOREIGN KEY (ref) REFERENCES schema{(i + 1) % (count / 100)}.t{(i + 1) % count}(id)"
            });
        }

        _model = new DatabaseModel
        {
            Schemas = schemas,
            Tables = tables,
            Types = types,
            Sequences = sequences,
            Constraints = constraints
        };

        _files = new FileWriteResult();
        _files.SchemaFiles.AddRange(schemas.Select(s => $"schemas/{SqlIdentifier.SafeFileName(s.Name)}.sql"));
        _files.TableFiles.AddRange(tableFiles);
        _files.TypeFiles.AddRange(typeFiles);
    }

    [Benchmark]
    public DeploymentPlan Build() => _builder.Build(_model, _files);
}
