using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;
using Xunit;

namespace PgSchemaExporter.Tests;

public class TypeScriptGeneratorTests
{
    private readonly TypeScriptGenerator _generator = new();

    [Fact]
    public void Generate_EmitsEnumCreation()
    {
        var type = new DbType
        {
            Schema = "public",
            Name = "mood",
            Kind = "e",
            EnumLabels = ["sad", "ok", "happy"]
        };

        var sql = _generator.Generate(type);

        Assert.Contains("CREATE TYPE", sql);
        Assert.Contains("AS ENUM", sql);
        Assert.Contains("'happy'", sql);
    }

    [Fact]
    public void Generate_UsesCompositeDefinition()
    {
        var type = new DbType
        {
            Schema = "public",
            Name = "point3d",
            Kind = "c",
            CompositeDefinition = "CREATE TYPE public.point3d AS (x double precision, y double precision, z double precision);"
        };

        var sql = _generator.Generate(type);

        Assert.Equal(type.CompositeDefinition, sql);
    }

    [Fact]
    public void Generate_UsesRangeDefinition()
    {
        var type = new DbType
        {
            Schema = "public",
            Name = "floatrange",
            Kind = "r",
            RangeDefinition = "CREATE TYPE public.floatrange AS RANGE (subtype = double precision);"
        };

        var sql = _generator.Generate(type);

        Assert.Equal(type.RangeDefinition, sql);
    }

    [Fact]
    public void Generate_ReturnsCommentForUnsupportedKind()
    {
        var type = new DbType
        {
            Schema = "public",
            Name = "unknown",
            Kind = "d"
        };

        var sql = _generator.Generate(type);

        Assert.Contains("Unsupported type kind", sql);
    }
}
