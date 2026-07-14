using PgSchemaExporter.Core.Diagramming;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PgSchemaExporter.Tests;

public class DiagrammingTests
{
    [Theory]
    [InlineData("PRIMARY KEY (id, tenant_id)", ConstraintKind.PrimaryKey, "id,tenant_id")]
    [InlineData("UNIQUE (email)", ConstraintKind.Unique, "email")]
    [InlineData("FOREIGN KEY (post_id) REFERENCES posts(id)", ConstraintKind.ForeignKey, "post_id")]
    [InlineData("FOREIGN KEY (\"post_id\") REFERENCES \"public\".\"posts\" (\"id\")", ConstraintKind.ForeignKey, "post_id")]
    [InlineData("CHECK (length(name) > 0)", ConstraintKind.Other, "")]
    public void ConstraintDefinitionParser_ParsesKindsAndColumns(string definition, ConstraintKind expectedKind, string columnsCsv)
    {
        var parsed = ConstraintDefinitionParser.Parse(definition);

        Assert.Equal(expectedKind, parsed.Kind);
        var expectedColumns = columnsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expectedColumns, parsed.Columns);
    }

    [Fact]
    public void ConstraintDefinitionParser_ForeignKey_ParsesReferencedTableAndColumns()
    {
        var parsed = ConstraintDefinitionParser.Parse(
            "FOREIGN KEY (\"post_id\") REFERENCES \"public\".\"posts\" (\"id\") ON DELETE CASCADE");

        Assert.Equal(ConstraintKind.ForeignKey, parsed.Kind);
        Assert.Equal(["post_id"], parsed.Columns);
        Assert.Equal("public.posts", parsed.ReferencedTable);
        Assert.Equal(["id"], parsed.ReferencedColumns);
    }

    [Fact]
    public void ErModelBuilder_FromDatabaseModel_BuildsTablesAndRelationships()
    {
        var model = new DatabaseModel
        {
            Tables =
            [
                new DbTable
                {
                    Schema = "public",
                    Name = "posts",
                    Columns =
                    [
                        new DbColumn { Name = "id", DataType = "integer", IsNullable = false, OrdinalPosition = 1 },
                        new DbColumn { Name = "title", DataType = "text", IsNullable = false, OrdinalPosition = 2 }
                    ]
                },
                new DbTable
                {
                    Schema = "public",
                    Name = "comments",
                    Columns =
                    [
                        new DbColumn { Name = "id", DataType = "integer", IsNullable = false, OrdinalPosition = 1 },
                        new DbColumn { Name = "post_id", DataType = "integer", IsNullable = false, OrdinalPosition = 2 },
                        new DbColumn { Name = "body", DataType = "text", IsNullable = true, OrdinalPosition = 3 }
                    ]
                }
            ],
            Constraints =
            [
                new DbConstraint { Schema = "public", TableName = "posts", Name = "posts_pkey", Type = "PRIMARY KEY", Definition = "PRIMARY KEY (id)" },
                new DbConstraint { Schema = "public", TableName = "comments", Name = "comments_pkey", Type = "PRIMARY KEY", Definition = "PRIMARY KEY (id)" },
                new DbConstraint { Schema = "public", TableName = "comments", Name = "comments_fk", Type = "FOREIGN KEY", Definition = "FOREIGN KEY (post_id) REFERENCES posts(id)" }
            ]
        };

        var er = ErModelBuilder.FromDatabaseModel(model);

        Assert.Equal(2, er.Tables.Count);
        var comments = er.Tables.Single(t => t.Name == "comments");
        var postId = comments.Columns.Single(c => c.Name == "post_id");
        Assert.True(postId.IsForeignKey);
        Assert.True(postId.IsPrimaryKey == false);
        Assert.True(comments.Columns.Single(c => c.Name == "id").IsPrimaryKey);

        Assert.Single(er.Relationships);
        var rel = er.Relationships[0];
        Assert.Equal("public.comments", rel.FromTable);
        Assert.Equal("public.posts", rel.ToTable);
        Assert.Equal(["post_id"], rel.FromColumns);
        Assert.Equal(["id"], rel.ToColumns);
        Assert.True(rel.IsMandatory);
    }

    [Fact]
    public void ErModelBuilder_FromDirectory_BuildsTablesAndRelationships()
    {
        var directory = CreateTemporarySchemaDirectory();
        try
        {
            var er = ErModelBuilder.FromDirectory(directory);

            Assert.Equal(2, er.Tables.Count);
            Assert.Contains(er.Tables, t => t.Name == "posts");
            Assert.Contains(er.Tables, t => t.Name == "comments");

            Assert.Single(er.Relationships);
            Assert.Equal("public.comments", er.Relationships[0].FromTable);
            Assert.Equal("public.posts", er.Relationships[0].ToTable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MermaidErRenderer_ProducesValidErDiagram()
    {
        var model = SampleModel();
        var output = MermaidErRenderer.Render(model);

        Assert.Contains("erDiagram", output);
        Assert.Contains("posts", output);
        Assert.Contains("comments", output);
        Assert.Contains("integer", output);
        Assert.Contains("||--o{", output);
        Assert.Contains(':', output); // column separator
    }

    [Fact]
    public void DotErRenderer_ProducesValidDigraph()
    {
        var model = SampleModel();
        var output = DotErRenderer.Render(model);

        Assert.Contains("digraph schema", output);
        Assert.Contains("public.posts", output);
        Assert.Contains("public.comments", output);
        Assert.Contains("->", output);
        Assert.Contains("arrowhead=crow", output);
    }

    [Fact]
    public async Task SchemaDiagramGenerator_FromLiveDatabase_RendersDiagram()
    {
        var provider = new TestMetadataProvider(SampleDatabaseModel());
        var generator = new SchemaDiagramGenerator(provider);

        var output = await generator.GenerateAsync(new DiagramOptions
        {
            ConnectionString = "fake",
            Format = DiagramFormat.Mermaid
        });

        Assert.Contains("erDiagram", output);
        Assert.Contains("posts", output);
        Assert.Contains("comments", output);
    }

    [Fact]
    public async Task SchemaDiagramGenerator_FromDirectory_RendersDiagram()
    {
        var directory = CreateTemporarySchemaDirectory();
        try
        {
            var generator = new SchemaDiagramGenerator();
            var output = await generator.GenerateAsync(new DiagramOptions
            {
                SchemaDirectory = directory,
                Format = DiagramFormat.Dot
            });

            Assert.Contains("digraph schema", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporarySchemaDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pgschema-diagram-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);

        var tables = Path.Combine(directory, "tables");
        var constraints = Path.Combine(directory, "constraints");
        Directory.CreateDirectory(tables);
        Directory.CreateDirectory(constraints);

        File.WriteAllText(Path.Combine(tables, "posts.sql"),
            """
            CREATE TABLE "public"."posts" (
                "id" integer NOT NULL,
                "title" text NOT NULL
            );
            """);

        File.WriteAllText(Path.Combine(tables, "comments.sql"),
            """
            CREATE TABLE "public"."comments" (
                "id" integer NOT NULL,
                "post_id" integer NOT NULL,
                "body" text
            );
            """);

        File.WriteAllText(Path.Combine(constraints, "comments_fk.sql"),
            """
            ALTER TABLE "public"."comments" ADD CONSTRAINT comments_fk
                FOREIGN KEY ("post_id") REFERENCES "public"."posts" ("id");
            """);

        return directory;
    }

    private static DatabaseModel SampleDatabaseModel() => new()
    {
        Tables =
        [
            new DbTable
            {
                Schema = "public",
                Name = "posts",
                Columns =
                [
                    new DbColumn { Name = "id", DataType = "integer", IsNullable = false, OrdinalPosition = 1 },
                    new DbColumn { Name = "title", DataType = "text", IsNullable = false, OrdinalPosition = 2 }
                ]
            },
            new DbTable
            {
                Schema = "public",
                Name = "comments",
                Columns =
                [
                    new DbColumn { Name = "id", DataType = "integer", IsNullable = false, OrdinalPosition = 1 },
                    new DbColumn { Name = "post_id", DataType = "integer", IsNullable = false, OrdinalPosition = 2 },
                    new DbColumn { Name = "body", DataType = "text", IsNullable = true, OrdinalPosition = 3 }
                ]
            }
        ],
        Constraints =
        [
            new DbConstraint { Schema = "public", TableName = "posts", Name = "posts_pkey", Definition = "PRIMARY KEY (id)" },
            new DbConstraint { Schema = "public", TableName = "comments", Name = "comments_pkey", Definition = "PRIMARY KEY (id)" },
            new DbConstraint { Schema = "public", TableName = "comments", Name = "comments_fk", Definition = "FOREIGN KEY (post_id) REFERENCES posts(id)" }
        ]
    };

    private static ErModel SampleModel() => new()
    {
        Tables =
        [
            new ErTable
            {
                Schema = "public",
                Name = "posts",
                Columns =
                [
                    new ErColumn { Name = "id", DataType = "integer", IsNullable = false, IsPrimaryKey = true },
                    new ErColumn { Name = "title", DataType = "text", IsNullable = false }
                ]
            },
            new ErTable
            {
                Schema = "public",
                Name = "comments",
                Columns =
                [
                    new ErColumn { Name = "id", DataType = "integer", IsNullable = false, IsPrimaryKey = true },
                    new ErColumn { Name = "post_id", DataType = "integer", IsNullable = false, IsForeignKey = true },
                    new ErColumn { Name = "body", DataType = "text", IsNullable = true }
                ]
            }
        ],
        Relationships =
        [
            new ErRelationship
            {
                FromTable = "public.comments",
                FromColumns = ["post_id"],
                ToTable = "public.posts",
                ToColumns = ["id"],
                IsMandatory = true
            }
        ]
    };

    private sealed class TestMetadataProvider : IMetadataProvider
    {
        private readonly DatabaseModel _model;

        public TestMetadataProvider(DatabaseModel model)
        {
            _model = model;
        }

        public Task<DatabaseModel> LoadAsync(
            string connectionString,
            ExportOptions options,
            IProgressReporter? progress,
            Microsoft.Extensions.Logging.ILogger? logger,
            CancellationToken cancellationToken)
        {
            progress?.Start("Loading metadata");
            progress?.Step("Loading tables");
            progress?.Step("Loading constraints");
            progress?.Complete("Done");
            return Task.FromResult(_model);
        }
    }
}
