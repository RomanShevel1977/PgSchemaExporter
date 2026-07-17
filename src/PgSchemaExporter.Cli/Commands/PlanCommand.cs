using PgSchemaExporter.Core.Migration.Plan;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>plan</c> command.
/// </summary>
public sealed class PlanCommand : ICommand
{
    public string Name => "plan";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var (migrateOptions, planFile, format) = CliParser.ParsePlanOptions(context.Args);

        var planner = new MigrationPlanner();
        var plan = planner.CreatePlan(migrateOptions);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(MigrationPlanFile.Serialize(plan));
        else
            Console.WriteLine(MigrationPlanRenderer.RenderHuman(plan));

        if (!string.IsNullOrWhiteSpace(planFile))
        {
            await MigrationPlanFile.WriteAsync(planFile, plan);
            Console.WriteLine($"Plan written to: {Path.GetFullPath(planFile)}");
        }

        // Exit code 2 signals there are pending changes (useful for CI gating).
        return plan.HasChanges ? 2 : 0;
    }
}
