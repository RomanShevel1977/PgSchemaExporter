using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Hazards;
using Xunit;

namespace PgSchemaExporter.Tests;

public class HazardAnalyzerTests
{
    [Fact]
    public void Analyze_DropTable_FlagsHighTableDrop()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Table, "DROP TABLE IF EXISTS public.users;", isDestructive: true)
        });

        var hazard = Assert.Single(hazards);
        Assert.Equal(HazardCategory.TableDrop, hazard.Category);
        Assert.Equal(HazardSeverity.High, hazard.Severity);
    }

    [Fact]
    public void Analyze_DropColumn_FlagsHighColumnDrop()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Table, "ALTER TABLE public.users DROP COLUMN age;", isDestructive: true)
        });

        Assert.Equal(HazardCategory.ColumnDrop, Assert.Single(hazards).Category);
    }

    [Fact]
    public void Analyze_AlterColumnType_FlagsTypeChange()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Table, "ALTER TABLE public.users ALTER COLUMN id TYPE bigint;")
        });

        var hazard = Assert.Single(hazards);
        Assert.Equal(HazardCategory.TypeChange, hazard.Category);
        Assert.Equal(HazardSeverity.High, hazard.Severity);
    }

    [Fact]
    public void Analyze_SetNotNull_FlagsMediumNotNull()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Table, "ALTER TABLE public.users ALTER COLUMN email SET NOT NULL;")
        });

        var hazard = Assert.Single(hazards);
        Assert.Equal(HazardCategory.NotNull, hazard.Category);
        Assert.Equal(HazardSeverity.Medium, hazard.Severity);
    }

    [Fact]
    public void Analyze_NonConcurrentIndex_FlagsIndexBuild()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "CREATE INDEX idx ON public.t (a);")
        });

        Assert.Equal(HazardCategory.IndexBuild, Assert.Single(hazards).Category);
    }

    [Fact]
    public void Analyze_ConcurrentIndex_NoIndexBuildHazard()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Index, "CREATE INDEX CONCURRENTLY idx ON public.t (a);", runsOutsideTransaction: true)
        });

        Assert.Empty(hazards);
    }

    [Fact]
    public void Analyze_SafeCreateTable_NoHazards()
    {
        var hazards = HazardAnalyzer.Analyze(new List<MigrationStatement>
        {
            new(MigrationObjectKind.Table, "CREATE TABLE public.t (id int);")
        });

        Assert.Empty(hazards);
    }

    [Fact]
    public void Analyze_UsesUpDirection()
    {
        var script = new MigrationScript
        {
            Up = [new(MigrationObjectKind.Table, "DROP TABLE public.t;", isDestructive: true)],
            Down = [new(MigrationObjectKind.Table, "CREATE TABLE public.t (id int);")]
        };

        var hazards = HazardAnalyzer.Analyze(script);

        Assert.Equal(HazardCategory.TableDrop, Assert.Single(hazards).Category);
    }
}
