using System.Text;

namespace PgSchemaExporter.Core.Migration.Plan;

/// <summary>
/// Renders a <see cref="MigrationPlan"/> as a human-readable, Terraform-style
/// summary suitable for review in a terminal or a pull request.
/// </summary>
public static class MigrationPlanRenderer
{
    public static string RenderHuman(MigrationPlan plan)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Migration plan");
        sb.AppendLine("==============");
        sb.AppendLine($"From:        {plan.FromDirectory}");
        sb.AppendLine($"To:          {plan.ToDirectory}");
        if (!string.IsNullOrWhiteSpace(plan.Name))
            sb.AppendLine($"Name:        {plan.Name}");
        sb.AppendLine($"Generated:   {plan.GeneratedAt:u}");
        sb.AppendLine($"Online DDL:  {(plan.Settings.OnlineDdl ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(plan.Settings.LockTimeout))
            sb.AppendLine($"Lock timeout:      {plan.Settings.LockTimeout}");
        if (!string.IsNullOrWhiteSpace(plan.Settings.StatementTimeout))
            sb.AppendLine($"Statement timeout: {plan.Settings.StatementTimeout}");
        sb.AppendLine();

        if (!plan.HasChanges)
        {
            sb.AppendLine("No changes. Schema is up to date.");
            return sb.ToString();
        }

        var creates = plan.Up.Count(s => !s.Destructive);
        var destroys = plan.Up.Count(s => s.Destructive);
        sb.AppendLine($"Plan: {plan.Up.Count} statement(s) to apply — {creates} safe, {destroys} destructive.");
        sb.AppendLine();

        sb.AppendLine("Statements (up):");
        foreach (var statement in plan.Up)
        {
            var marker = statement.Destructive ? "  - [DESTRUCTIVE]" : "  + ";
            var suffix = statement.RunsOutsideTransaction ? "  (runs outside transaction)" : "";
            sb.AppendLine($"{marker} {FirstLine(statement.Sql)}{suffix}");
        }
        sb.AppendLine();

        if (plan.Hazards.Count > 0)
        {
            sb.AppendLine($"Hazards ({plan.Hazards.Count}):");
            foreach (var hazard in plan.Hazards.OrderByDescending(SeverityRank))
            {
                sb.AppendLine($"  [{hazard.Severity.ToUpperInvariant()}] {hazard.Category}: {hazard.Message}");
                sb.AppendLine($"      {FirstLine(hazard.Statement)}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static int SeverityRank(PlanHazard hazard) => hazard.Severity.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    private static string FirstLine(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var newline = normalized.IndexOf('\n');
        var line = newline < 0 ? normalized : normalized[..newline] + " …";
        return line.Length <= 160 ? line : line[..160] + "…";
    }
}
