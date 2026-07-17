using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Migration.Hazards;

/// <summary>
/// Inspects the statements of a generated <see cref="MigrationScript"/> and flags
/// operations that are destructive or that take heavy locks in PostgreSQL, so the
/// user can review them before applying to production.
/// </summary>
public static class HazardAnalyzer
{
    /// <summary>Analyzes the up migration (the direction applied to production).</summary>
    public static IReadOnlyList<Hazard> Analyze(MigrationScript script)
        => Analyze(script.Up);

    public static IReadOnlyList<Hazard> Analyze(IReadOnlyList<MigrationStatement> statements)
    {
        var hazards = new List<Hazard>();

        foreach (var statement in statements)
        {
            var sql = statement.Sql;
            var single = SqlTokenizer.NormalizeStatement(sql);

            if (ContainsPhrase(sql, "DROP FOREIGN TABLE") || ContainsPhrase(sql, "DROP TABLE"))
            {
                hazards.Add(Make(HazardCategory.TableDrop, HazardSeverity.High,
                    "Dropping a table permanently deletes all of its data.", single));
                continue;
            }

            if (ContainsPhrase(sql, "DROP COLUMN"))
            {
                hazards.Add(Make(HazardCategory.ColumnDrop, HazardSeverity.High,
                    "Dropping a column permanently deletes its data.", single));
                continue;
            }

            if (ContainsAfter(sql, "ALTER COLUMN", "TYPE"))
            {
                hazards.Add(Make(HazardCategory.TypeChange, HazardSeverity.High,
                    "Changing a column type rewrites the table and holds an ACCESS EXCLUSIVE lock.", single));
                continue;
            }

            if (ContainsPhrase(sql, "SET NOT NULL"))
            {
                hazards.Add(Make(HazardCategory.NotNull, HazardSeverity.Medium,
                    "Adding NOT NULL scans the whole table while holding a lock.", single));
                continue;
            }

            if ((ContainsPhrase(sql, "CREATE INDEX") || ContainsPhrase(sql, "CREATE UNIQUE INDEX"))
                && !ContainsPhrase(sql, "CONCURRENTLY"))
            {
                hazards.Add(Make(HazardCategory.IndexBuild, HazardSeverity.Medium,
                    "Building an index without CONCURRENTLY blocks writes for the duration. Consider --online-ddl.", single));
                continue;
            }

            if (statement.IsDestructive && DropAny(sql))
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

    private static bool ContainsPhrase(string sql, string phrase)
        => SqlTokenizer.IndexOfWord(sql, phrase) >= 0;

    private static bool ContainsAfter(string sql, string afterPhrase, string word)
    {
        var index = SqlTokenizer.IndexAfterWord(sql, afterPhrase);
        return index >= 0 && SqlTokenizer.IndexOfWord(sql, word, index) >= 0;
    }

    private static bool DropAny(string sql)
    {
        var index = 0;
        while ((index = SqlTokenizer.IndexOfWord(sql, "DROP", index, out var length)) >= 0)
        {
            index += length;
            index = SkipWhitespace(sql, index);
            var token = SqlTokenizer.ReadIdentifier(sql, index, out var after, unquote: false);
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (token.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
                || token.Equals("COLUMN", StringComparison.OrdinalIgnoreCase))
            {
                index = after;
                continue;
            }

            if (token.Equals("FOREIGN", StringComparison.OrdinalIgnoreCase))
            {
                var nextIndex = SkipWhitespace(sql, after);
                var nextToken = SqlTokenizer.ReadIdentifier(sql, nextIndex, out var afterNext, unquote: false);
                if (nextToken != null && nextToken.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    index = afterNext;
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    private static int SkipWhitespace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
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
