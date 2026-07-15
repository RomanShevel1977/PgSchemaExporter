using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Migration;
using PgSchemaExporter.Core.Migration.Plan;
using Xunit;

namespace PgSchemaExporter.Tests;

public class MigrationApplierTests
{
    [Fact]
    public async Task ApplyAsync_EmptyPlan_ReturnsZeroAndDryRunFlag()
    {
        var applier = new MigrationApplier();
        var plan = new MigrationPlan
        {
            Settings = new MigrationPlanSettings(),
            Up = [],
            Down = []
        };

        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = "Host=localhost;Database=db;Username=postgres",
            DryRun = false
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Equal(0, result.Executed);
        Assert.Equal(0, result.Skipped);
        Assert.False(result.DryRun);
    }

    [Fact]
    public async Task ApplyAsync_DryRun_ExecutesNoStatements()
    {
        var applier = new MigrationApplier();
        var plan = new MigrationPlan
        {
            Settings = new MigrationPlanSettings(),
            Up =
            [
                new PlanStatement { Sql = "CREATE TABLE t (id int);", Destructive = false },
                new PlanStatement { Sql = "CREATE INDEX idx ON t (id);", Destructive = false, RunsOutsideTransaction = true }
            ]
        };

        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = "Host=localhost;Database=db;Username=postgres",
            DryRun = true
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Equal(0, result.Executed);
        Assert.Equal(0, result.Skipped);
        Assert.True(result.DryRun);
    }

    [Fact]
    public async Task ApplyAsync_SafePlan_SkipsDestructiveStatements()
    {
        var applier = new MigrationApplier();
        var plan = new MigrationPlan
        {
            Settings = new MigrationPlanSettings { Safe = true },
            Up =
            [
                new PlanStatement { Sql = "DROP TABLE t;", Destructive = true },
                new PlanStatement { Sql = "CREATE TABLE t (id int);", Destructive = false }
            ]
        };

        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = "Host=localhost;Database=db;Username=postgres",
            DryRun = true
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Equal(0, result.Executed);
        Assert.Equal(1, result.Skipped);
        Assert.True(result.DryRun);
    }

    [Fact]
    public async Task ApplyAsync_Rollback_UsesDownStatements()
    {
        var applier = new MigrationApplier();
        var plan = new MigrationPlan
        {
            Settings = new MigrationPlanSettings(),
            Up = [new PlanStatement { Sql = "CREATE TABLE t (id int);" }],
            Down = [new PlanStatement { Sql = "DROP TABLE t;" }]
        };

        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = "Host=localhost;Database=db;Username=postgres",
            Rollback = true,
            DryRun = true
        }, NullProgressReporter.Instance, NullLogger.Instance);

        Assert.Equal(0, result.Executed);
        Assert.Equal(0, result.Skipped);
        Assert.True(result.DryRun);
    }

    [Fact]
    public async Task ApplyAsync_InvalidTimeout_Throws()
    {
        var applier = new MigrationApplier();
        var plan = new MigrationPlan
        {
            Settings = new MigrationPlanSettings { LockTimeout = "drop table" },
            Up = []
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = "Host=localhost;Database=db;Username=postgres"
        }, NullProgressReporter.Instance, NullLogger.Instance));

        Assert.Contains("Invalid timeout value", ex.Message);
    }
}
