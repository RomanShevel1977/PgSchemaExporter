using System.Text.RegularExpressions;

namespace PgSchemaExporter.Core.Migration.Hazards;

/// <summary>
/// Inspects the statements of a generated <see cref="MigrationScript"/> and flags
/// operations that are destructive or that take heavy locks in PostgreSQL, so the
/// user can review them before applying to production.
/// </summary>
public static partial class HazardAnalyzer
{
    [GeneratedRegex(@"\bDROP\s+(FOREIGN\s+)?TABLE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropTableRegex();

    [GeneratedRegex(@"\bDROP\s+COLUMN\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropColumnRegex();

    [GeneratedRegex(@"\bALTER\s+COLUMN\b[^;]*\bTYPE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AlterTypeRegex();

    [GeneratedRegex(@"\bSET\s+NOT\s+NULL\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SetNotNullRegex();

    [GeneratedRegex(@"\bCREATE\s+(UNIQUE\s+)?INDEX\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CreateIndexRegex();

    [GeneratedRegex(@"\bCONCURRENTLY\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ConcurrentlyRegex();

    [GeneratedRegex(@"\bDROP\s+\w+\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropAnyRegex();

    /// <summary>Analyzes the up migration (the direction applied to production).</summary>
    public static IReadOnlyList<Hazard> Analyze(MigrationScript script)
        => Analyze(script.Up);

    public static IReadOnlyList<Hazard> Analyze(IReadOnlyList<MigrationStatement> statements)
    {
        var hazards = new List<Hazard>();

        foreach (var statement in statements)
        {
            var sql = statement.Sql;
            var single = Regex.Replace(sql.Replace("\r\n", " ").Replace('\n', ' '), @"\s+", " ").Trim();

            if (DropTableRegex().IsMatch(sql))
            {
                hazards.Add(Make(HazardCategory.TableDrop, HazardSeverity.High,
                    "Dropping a table permanently deletes all of its data.", single));
                continue;
            }

            if (DropColumnRegex().IsMatch(sql))
            {
                hazards.Add(Make(HazardCategory.ColumnDrop, HazardSeverity.High,
                    "Dropping a column permanently deletes its data.", single));
                continue;
            }

            if (AlterTypeRegex().IsMatch(sql))
            {
                hazards.Add(Make(HazardCategory.TypeChange, HazardSeverity.High,
                    "Changing a column type rewrites the table and holds an ACCESS EXCLUSIVE lock.", single));
                continue;
            }

            if (SetNotNullRegex().IsMatch(sql))
            {
                hazards.Add(Make(HazardCategory.NotNull, HazardSeverity.Medium,
                    "Adding NOT NULL scans the whole table while holding a lock.", single));
                continue;
            }

            if (CreateIndexRegex().IsMatch(sql) && !ConcurrentlyRegex().IsMatch(sql))
            {
                hazards.Add(Make(HazardCategory.IndexBuild, HazardSeverity.Medium,
                    "Building an index without CONCURRENTLY blocks writes for the duration. Consider --online-ddl.", single));
                continue;
            }

            if (statement.IsDestructive && DropAnyRegex().IsMatch(sql))
            {
                hazards.Add(Make(HazardCategory.ObjectDrop, HazardSeverity.Medium,
                    "Dropping this object may be irreversible.", single));
                continue;
            }

            if (statement.IsDestructive)
            {
                hazards.Add(Make(HazardCategory.DataLoss, HazardSeverity.Medium,
                    "This statement was flagged as potentially destructive.", single));
            }
        }

        return hazards;
    }

    private static Hazard Make(HazardCategory category, HazardSeverity severity, string message, string statement)
        => new()
        {
            Category = category,
            Severity = severity,
            Message = message,
            Statement = Truncate(statement, 200)
        };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
