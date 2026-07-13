using PgSchemaExporter.Core.Migration.Hazards;

namespace PgSchemaExporter.Core.Migration.Plan;

/// <summary>
/// A serializable, reviewable representation of a migration between two schema
/// states — the core of the declarative plan/apply workflow. A plan is generated
/// with <c>plan</c>, reviewed by a human or in a PR, and executed with <c>apply</c>.
/// </summary>
public sealed class MigrationPlan
{
    public string FromDirectory { get; set; } = "";
    public string ToDirectory { get; set; } = "";
    public string? Name { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Render/execution settings captured at plan time.</summary>
    public MigrationPlanSettings Settings { get; set; } = new();

    public IReadOnlyList<PlanStatement> Up { get; set; } = [];
    public IReadOnlyList<PlanStatement> Down { get; set; } = [];
    public IReadOnlyList<PlanHazard> Hazards { get; set; } = [];

    public bool HasChanges => Up.Count > 0;
    public bool HasDestructiveChanges => Up.Any(s => s.Destructive) || Down.Any(s => s.Destructive);
}

public sealed class MigrationPlanSettings
{
    public bool Safe { get; set; }
    public bool OnlineDdl { get; set; }
    public string? LockTimeout { get; set; }
    public string? StatementTimeout { get; set; }
}

public sealed class PlanStatement
{
    public string Kind { get; set; } = "";
    public string Sql { get; set; } = "";
    public bool Destructive { get; set; }
    public bool RunsOutsideTransaction { get; set; }
    public string? Comment { get; set; }
}

public sealed class PlanHazard
{
    public string Category { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string Statement { get; set; } = "";
}

internal static class PlanMappers
{
    public static PlanStatement ToPlanStatement(this MigrationStatement s) => new()
    {
        Kind = s.Kind.ToString(),
        Sql = s.Sql,
        Destructive = s.IsDestructive,
        RunsOutsideTransaction = s.RunsOutsideTransaction,
        Comment = s.Comment
    };

    public static PlanHazard ToPlanHazard(this Hazard h) => new()
    {
        Category = h.Category.ToString(),
        Severity = h.Severity.ToString(),
        Message = h.Message,
        Statement = h.Statement
    };
}
