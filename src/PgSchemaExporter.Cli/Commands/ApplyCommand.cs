using PgSchemaExporter.Core.Migration.Plan;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Handles the <c>apply</c> command.
/// </summary>
public sealed class ApplyCommand : ICommand
{
    public string Name => "apply";

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        var applyArgs = CliParser.ParseApplyOptions(context.Args);

        var plan = await MigrationPlanFile.ReadAsync(applyArgs.PlanFile);

        if (!plan.HasChanges)
        {
            Console.WriteLine("Plan contains no changes. Nothing to apply.");
            return 0;
        }

        PrintPlanHazards(plan.Hazards);

        if (!applyArgs.DryRun && !applyArgs.AssumeYes)
        {
            Console.Write($"Apply {(applyArgs.Rollback ? "rollback (down)" : "up")} migration to the target database? [y/N] ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted.");
                return 1;
            }
        }

        var applier = new MigrationApplier();
        var result = await applier.ApplyAsync(plan, new MigrationApplier.ApplyOptions
        {
            ConnectionString = applyArgs.ConnectionString,
            Rollback = applyArgs.Rollback,
            DryRun = applyArgs.DryRun
        }, context.Progress, context.Logger);

        if (result.DryRun)
            Console.WriteLine($"Dry run complete. {result.Skipped} destructive statement(s) would be skipped (safe plan).");
        else
            Console.WriteLine($"Applied {result.Executed} statement(s). Skipped {result.Skipped}.");

        return 0;
    }

    private static void PrintPlanHazards(IReadOnlyList<PlanHazard> hazards)
    {
        if (hazards.Count == 0)
            return;

        static int Rank(string severity) => severity.ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };

        Console.WriteLine($"Hazards detected ({hazards.Count}):");
        foreach (var hazard in hazards.OrderByDescending(h => Rank(h.Severity)))
        {
            Console.WriteLine($"  [{hazard.Severity.ToUpperInvariant()}] {hazard.Category}: {hazard.Message}");
            Console.WriteLine($"      {hazard.Statement}");
        }
        Console.WriteLine();
    }
}
