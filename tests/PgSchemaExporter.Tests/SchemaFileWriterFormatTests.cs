using System.Security.Cryptography;
using System.Text;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Output;
using Xunit;

namespace PgSchemaExporter.Tests;

public class SchemaFileWriterFormatTests : IDisposable
{
    private readonly string _output;

    public SchemaFileWriterFormatTests()
    {
        _output = Path.Combine(Path.GetTempPath(), "pgschema-writer-" + Guid.NewGuid().ToString("n"));
    }

    private static DatabaseModel BuildModel() => new()
    {
        Tables =
        [
            new DbTable
            {
                Schema = "public",
                Name = "users",
                Columns = [new DbColumn { Name = "id", DataType = "integer", IsNullable = false }]
            }
        ],
        Constraints =
        [
            new DbConstraint { Schema = "public", TableName = "users", Name = "users_pkey", Definition = "PRIMARY KEY (id)" }
        ],
        Indexes =
        [
            new DbIndex { Schema = "public", TableName = "users", Name = "users_idx", Definition = "CREATE INDEX users_idx ON public.users (id)" }
        ]
    };

    [Fact]
    public async Task UseIfNotExistsFalse_StripsIfNotExists()
    {
        var writer = new SchemaFileWriter();
        await writer.WriteAsync(_output, BuildModel(), new FormatOptions { UseIfNotExists = false }, CancellationToken.None);

        var tableSql = await File.ReadAllTextAsync(Path.Combine(_output, "tables", "public.users.sql"));
        Assert.DoesNotContain("IF NOT EXISTS", tableSql);
        Assert.Contains("CREATE TABLE", tableSql);
    }

    [Fact]
    public async Task SplitConstraintsAndIndexesFalse_InlinesIntoTableFile()
    {
        var writer = new SchemaFileWriter();
        var result = await writer.WriteAsync(
            _output,
            BuildModel(),
            new FormatOptions { SplitConstraints = false, SplitIndexes = false },
            CancellationToken.None);

        Assert.Empty(result.ConstraintFiles);
        Assert.Empty(result.IndexFiles);
        Assert.False(Directory.Exists(Path.Combine(_output, "constraints")));
        Assert.False(Directory.Exists(Path.Combine(_output, "indexes")));

        var tableSql = await File.ReadAllTextAsync(Path.Combine(_output, "tables", "public.users.sql"));
        Assert.Contains("ADD CONSTRAINT", tableSql);
        Assert.Contains("CREATE INDEX", tableSql);
    }

    [Fact]
    public async Task DefaultFormat_WritesSeparateConstraintAndIndexFiles()
    {
        var writer = new SchemaFileWriter();
        var result = await writer.WriteAsync(_output, BuildModel(), new FormatOptions(), CancellationToken.None);

        Assert.Single(result.ConstraintFiles);
        Assert.Single(result.IndexFiles);
    }

    [Fact]
    public async Task FunctionFileName_IsContentBasedAndDeterministic()
    {
        var model = new DatabaseModel
        {
            Functions =
            [
                new DbFunction
                {
                    Schema = "public",
                    Name = "do_work",
                    ArgumentsIdentity = "integer, text",
                    Definition = "CREATE FUNCTION public.do_work(integer, text) RETURNS void AS $$ BEGIN END; $$ LANGUAGE plpgsql;"
                }
            ]
        };

        var writer = new SchemaFileWriter();
        var result = await writer.WriteAsync(_output, model, new FormatOptions(), CancellationToken.None);

        // Independently derive the expected stable hash (first 8 bytes of SHA-256, hex).
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("integer, text"));
        var expectedHash = string.Concat(bytes.Take(8).Select(b => b.ToString("x2")));

        Assert.Equal($"functions/public.do_work.{expectedHash}.sql", result.FunctionFiles.Single());
    }

    public void Dispose()
    {
        if (Directory.Exists(_output))
            Directory.Delete(_output, recursive: true);
    }
}
