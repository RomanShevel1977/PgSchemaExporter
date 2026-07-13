using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Plan;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationPlanTests : IDisposable
{
    private readonly string _root;
    private readonly string _from;
    private readonly string _to;

    public MigrationPlanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pgschema-plan-" + Guid.NewGuid().ToString("n"));
        _from = Path.Combine(_root, "from");
        _to = Path.Combine(_root, "to");
        Directory.CreateDirectory(_from);
        Directory.CreateDirectory(_to);
    }

    [Fact]
    public void CreatePlan_AddedTable_CapturesUpAndDown()
    {
        Write(_to, "tables/public.users.sql", "CREATE TABLE \"public\".\"users\" (\n  \"id\" integer NOT NULL\n);");

        var plan = CreatePlan();

        Assert.True(plan.HasChanges);
        Assert.Contains(plan.Up, s => s.Sql.Contains("CREATE TABLE"));
        Assert.Contains(plan.Down, s => s.Sql.Contains("DROP TABLE"));
    }

    [Fact]
    public void CreatePlan_DropTable_ProducesHazard()
    {
        Write(_from, "tables/public.users.sql", "CREATE TABLE \"public\".\"users\" (\n  \"id\" integer NOT NULL\n);");

        var plan = CreatePlan();

        Assert.True(plan.HasDestructiveChanges);
        Assert.Contains(plan.Hazards, h => h.Category == "TableDrop");
    }

    [Fact]
    public void CreatePlan_CapturesSettings()
    {
        Write(_to, "indexes/public.idx.sql", "CREATE INDEX idx ON public.users (email);");

        var plan = CreatePlan(o =>
        {
            o.OnlineDdl = true;
            o.LockTimeout = "5s";
        });

        Assert.True(plan.Settings.OnlineDdl);
        Assert.Equal("5s", plan.Settings.LockTimeout);
        Assert.Contains(plan.Up, s => s.RunsOutsideTransaction && s.Sql.Contains("CONCURRENTLY"));
    }

    [Fact]
    public async Task PlanFile_RoundTrips()
    {
        Write(_to, "tables/public.users.sql", "CREATE TABLE \"public\".\"users\" (\n  \"id\" integer NOT NULL\n);");
        var plan = CreatePlan();
        var path = Path.Combine(_root, "plan.json");

        await MigrationPlanFile.WriteAsync(path, plan);
        var reloaded = await MigrationPlanFile.ReadAsync(path);

        Assert.Equal(plan.Up.Count, reloaded.Up.Count);
        Assert.Equal(plan.HasChanges, reloaded.HasChanges);
        Assert.Equal(plan.FromDirectory, reloaded.FromDirectory);
    }

    [Fact]
    public async Task PlanFile_Read_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            MigrationPlanFile.ReadAsync(Path.Combine(_root, "missing.json")));
    }

    [Fact]
    public void RenderHuman_NoChanges_SaysUpToDate()
    {
        var plan = CreatePlan();

        var text = MigrationPlanRenderer.RenderHuman(plan);

        Assert.Contains("No changes", text);
    }

    [Fact]
    public void RenderHuman_WithChanges_ListsStatementsAndHazards()
    {
        Write(_from, "tables/public.users.sql", "CREATE TABLE \"public\".\"users\" (\n  \"id\" integer NOT NULL\n);");

        var plan = CreatePlan();
        var text = MigrationPlanRenderer.RenderHuman(plan);

        Assert.Contains("Statements (up):", text);
        Assert.Contains("Hazards", text);
        Assert.Contains("DROP TABLE", text);
    }

    private MigrationPlan CreatePlan(Action<MigrationOptions>? configure = null)
    {
        var options = new MigrationOptions
        {
            FromDirectory = _from,
            ToDirectory = _to,
            Preview = true
        };
        configure?.Invoke(options);
        return new MigrationPlanner().CreatePlan(options);
    }

    private static void Write(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
