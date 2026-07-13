using PgSchemaExporter.Core.Migration.Hazards;

namespace PgSchemaExporter.Core.Migration.Plan;

/// <summary>
/// Builds a reviewable <see cref="MigrationPlan"/> from migration options by
/// generating the up/down script and running a hazard analysis over it.
/// </summary>
public sealed class MigrationPlanner
{
    private readonly MigrationGenerator _generator = new();

    public MigrationPlan CreatePlan(MigrationOptions options)
    {
        options.EnsureValid();

        var script = _generator.Generate(options);
        var hazards = HazardAnalyzer.Analyze(script);

        return new MigrationPlan
        {
            FromDirectory = options.FromDirectory,
            ToDirectory = options.ToDirectory,
            Name = options.Name,
            GeneratedAt = DateTimeOffset.UtcNow,
            Settings = new MigrationPlanSettings
            {
                Safe = options.Safe,
                OnlineDdl = options.OnlineDdl,
                LockTimeout = options.LockTimeout,
                StatementTimeout = options.StatementTimeout
            },
            Up = script.Up.Select(s => s.ToPlanStatement()).ToList(),
            Down = script.Down.Select(s => s.ToPlanStatement()).ToList(),
            Hazards = hazards.Select(h => h.ToPlanHazard()).ToList()
        };
    }
}
