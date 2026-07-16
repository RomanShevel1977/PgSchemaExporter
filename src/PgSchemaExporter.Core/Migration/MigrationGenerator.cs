using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Compares two exported schema directories (baseline vs. target) and produces an
/// ordered up/down <see cref="MigrationScript"/>. Tables are diffed semantically at
/// the column level; other objects use create/replace or drop/recreate strategies
/// appropriate to their kind.
/// </summary>
public sealed class MigrationGenerator
{
    private readonly SqlStatementCache _statementCache = new();

    public MigrationScript Generate(MigrationOptions options)
    {
        options.EnsureValid();

        var from = Enumerate(options.FromDirectory);
        var to = Enumerate(options.ToDirectory);

        var up = new List<MigrationStatement>();
        var down = new List<MigrationStatement>();

        var allPaths = new SortedSet<string>(from.Keys, StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(to.Keys);

        foreach (var path in allPaths)
        {
            var hasFrom = from.TryGetValue(path, out var fromContent);
            var hasTo = to.TryGetValue(path, out var toContent);
            var kind = MigrationObjectKinds.FromRelativePath(path);

            if (hasTo && !hasFrom)
                BuildAdded(kind, toContent!, up, down);
            else if (hasFrom && !hasTo)
                BuildRemoved(kind, fromContent!, up, down);
            else if (Normalize(fromContent!) != Normalize(toContent!))
                BuildChanged(kind, fromContent!, toContent!, up, down);
        }

        var orderedUp = up.OrderBy(s => (int)s.Kind).ToList();
        var orderedDown = down.OrderByDescending(s => (int)s.Kind).ToList();

        if (options.OnlineDdl)
        {
            orderedUp = OnlineDdlRewriter.Rewrite(orderedUp);
            orderedDown = OnlineDdlRewriter.Rewrite(orderedDown);
        }

        return new MigrationScript
        {
            Up = orderedUp,
            Down = orderedDown
        };
    }

    private void BuildAdded(MigrationObjectKind kind, string content, List<MigrationStatement> up, List<MigrationStatement> down)
    {
        foreach (var statement in SplitStatements(content))
            up.Add(new MigrationStatement(kind, statement));

        foreach (var drop in SqlDropBuilder.BuildDrops(kind, SplitStatements(content)))
            down.Add(new MigrationStatement(kind, drop, IsDestructiveDrop(kind)));
    }

    private void BuildRemoved(MigrationObjectKind kind, string content, List<MigrationStatement> up, List<MigrationStatement> down)
    {
        foreach (var drop in SqlDropBuilder.BuildDrops(kind, SplitStatements(content)))
            up.Add(new MigrationStatement(kind, drop, IsDestructiveDrop(kind)));

        foreach (var statement in SplitStatements(content))
            down.Add(new MigrationStatement(kind, statement));
    }

    private void BuildChanged(
        MigrationObjectKind kind,
        string fromContent,
        string toContent,
        List<MigrationStatement> up,
        List<MigrationStatement> down)
    {
        switch (kind)
        {
            case MigrationObjectKind.Table:
                BuildChangedTable(fromContent, toContent, up, down);
                return;

            case MigrationObjectKind.Function:
            case MigrationObjectKind.Comment:
                // CREATE OR REPLACE / COMMENT overwrite in place: applying the target is enough.
                BuildReplace(kind, fromContent, toContent, up, down);
                return;

            case MigrationObjectKind.View when !IsMaterializedView(toContent) && !IsMaterializedView(fromContent):
                BuildReplace(kind, fromContent, toContent, up, down);
                return;

            default:
                BuildStatementSetDiff(kind, fromContent, toContent, up, down);
                return;
        }
    }

    private void BuildChangedTable(
        string fromContent,
        string toContent,
        List<MigrationStatement> up,
        List<MigrationStatement> down)
    {
        var fromTable = TableDefinitionParser.Parse(fromContent);
        var toTable = TableDefinitionParser.Parse(toContent);

        if (fromTable is { IsParseable: true } && toTable is { IsParseable: true })
        {
            var result = TableMigrationBuilder.Build(fromTable, toTable);
            up.AddRange(result.Up);
            down.AddRange(result.Down);
            return;
        }

        // Fallback: drop and recreate (destructive). Used when the table contains
        // constructs the parser does not handle semantically.
        foreach (var drop in SqlDropBuilder.BuildDrops(MigrationObjectKind.Table, SplitStatements(fromContent)))
            up.Add(new MigrationStatement(MigrationObjectKind.Table, drop, isDestructive: true, comment: "Table not semantically parseable; recreating"));
        foreach (var statement in SplitStatements(toContent))
            up.Add(new MigrationStatement(MigrationObjectKind.Table, statement));

        foreach (var drop in SqlDropBuilder.BuildDrops(MigrationObjectKind.Table, SplitStatements(toContent)))
            down.Add(new MigrationStatement(MigrationObjectKind.Table, drop, isDestructive: true));
        foreach (var statement in SplitStatements(fromContent))
            down.Add(new MigrationStatement(MigrationObjectKind.Table, statement));
    }

    private void BuildReplace(
        MigrationObjectKind kind,
        string fromContent,
        string toContent,
        List<MigrationStatement> up,
        List<MigrationStatement> down)
    {
        foreach (var statement in SplitStatements(toContent))
            up.Add(new MigrationStatement(kind, statement));

        foreach (var statement in SplitStatements(fromContent))
            down.Add(new MigrationStatement(kind, statement));
    }

    private void BuildStatementSetDiff(
        MigrationObjectKind kind,
        string fromContent,
        string toContent,
        List<MigrationStatement> up,
        List<MigrationStatement> down)
    {
        var fromStatements = SplitStatements(fromContent);
        var toStatements = SplitStatements(toContent);

        var fromKeys = fromStatements.Select(NormalizeStatement).ToHashSet(StringComparer.Ordinal);
        var toKeys = toStatements.Select(NormalizeStatement).ToHashSet(StringComparer.Ordinal);

        // Up: drop statements that no longer exist, then add new ones.
        foreach (var statement in fromStatements)
        {
            if (toKeys.Contains(NormalizeStatement(statement)))
                continue;

            var drop = SqlDropBuilder.BuildDrop(kind, statement);
            if (drop is not null)
                up.Add(new MigrationStatement(kind, drop, IsDestructiveDrop(kind)));
        }

        foreach (var statement in toStatements)
        {
            if (fromKeys.Contains(NormalizeStatement(statement)))
                continue;

            up.Add(new MigrationStatement(kind, statement));

            // Down: drop the newly added statement.
            var drop = SqlDropBuilder.BuildDrop(kind, statement);
            if (drop is not null)
                down.Add(new MigrationStatement(kind, drop, IsDestructiveDrop(kind)));
        }

        // Down: re-create statements that were removed.
        foreach (var statement in fromStatements)
        {
            if (toKeys.Contains(NormalizeStatement(statement)))
                continue;

            down.Add(new MigrationStatement(kind, statement));
        }
    }

    private IReadOnlyList<string> SplitStatements(string content)
    {
        return _statementCache.SplitStatements(content);
    }

    private string NormalizeStatement(string statement)
    {
        return _statementCache.NormalizeStatement(statement);
    }

    private static bool IsDestructiveDrop(MigrationObjectKind kind) => kind switch
    {
        MigrationObjectKind.Table => true,
        MigrationObjectKind.ForeignTable => true,
        MigrationObjectKind.Type => true,
        MigrationObjectKind.Domain => true,
        MigrationObjectKind.Sequence => true,
        _ => false
    };

    private static bool IsMaterializedView(string content)
        => content.AsSpan().Contains("MATERIALIZED".AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Enumerate(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            // Migrations only diff object definitions, not generated deploy artifacts.
            if (relative.Equals("deploy.sql", StringComparison.OrdinalIgnoreCase))
                continue;

            map[relative] = File.ReadAllText(file);
        }

        return map;
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

}
