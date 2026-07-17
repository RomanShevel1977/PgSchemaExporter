using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Diff;

/// <summary>
/// Compares two exported schema directories (the git-native layout) and reports
/// which object files were added, removed, or changed between them.
/// Supports comparing directory-to-directory, directory-to-live-database, or
/// live-database-to-live-database.
/// </summary>
public sealed class SchemaDiffer
{
    private readonly LiveSchemaExporter _liveExporter;

    public SchemaDiffer(IMetadataProvider? metadataProvider = null)
    {
        _liveExporter = new LiveSchemaExporter(metadataProvider);
    }

    public async Task<SchemaDiffResult> DiffAsync(
        SchemaDiffOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= NullProgressReporter.Instance;
        logger ??= NullLogger.Instance;

        options.EnsureValid();

        var leftTempDir = string.Empty;
        var rightTempDir = string.Empty;

        try
        {
            var leftDir = options.LeftDirectory;
            if (!string.IsNullOrWhiteSpace(options.LeftConnectionString))
            {
                progress.Step("Exporting left database");
                leftDir = await _liveExporter.ExportToTempDirectoryAsync(
                    options.LeftConnectionString,
                    BuildExportOptions(options),
                    progress,
                    logger,
                    cancellationToken);
                leftTempDir = leftDir;
            }

            var rightDir = options.RightDirectory;
            if (!string.IsNullOrWhiteSpace(options.RightConnectionString))
            {
                progress.Step("Exporting right database");
                rightDir = await _liveExporter.ExportToTempDirectoryAsync(
                    options.RightConnectionString,
                    BuildExportOptions(options),
                    progress,
                    logger,
                    cancellationToken);
                rightTempDir = rightDir;
            }

            progress.Step("Comparing schemas");
            return DiffDirectories(leftDir, rightDir, options);
        }
        finally
        {
            if (!string.IsNullOrEmpty(leftTempDir))
                _liveExporter.CleanupTempDirectory(leftTempDir);

            if (!string.IsNullOrEmpty(rightTempDir))
                _liveExporter.CleanupTempDirectory(rightTempDir);
        }
    }

    public SchemaDiffResult Diff(SchemaDiffOptions options)
    {
        options.EnsureValid();

        if (!string.IsNullOrWhiteSpace(options.LeftConnectionString) ||
            !string.IsNullOrWhiteSpace(options.RightConnectionString))
            throw new InvalidOperationException("Use DiffAsync for live database comparisons.");

        return DiffDirectories(options.LeftDirectory, options.RightDirectory, options);
    }

    private static SchemaDiffResult DiffDirectories(string leftDir, string rightDir, SchemaDiffOptions options)
    {
        var left = Enumerate(leftDir);
        var right = Enumerate(rightDir);

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new ConcurrentBag<string>();
        var unchanged = new ConcurrentBag<string>();
        var fileDiffs = new ConcurrentBag<FileDiff>();

        var common = new List<string>();
        foreach (var relativePath in right.Keys)
        {
            if (left.ContainsKey(relativePath))
                common.Add(relativePath);
            else
                added.Add(relativePath);
        }

        foreach (var relativePath in left.Keys)
        {
            if (!right.ContainsKey(relativePath))
                removed.Add(relativePath);
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Parallel ? Environment.ProcessorCount : 1
        };

        Parallel.ForEach(common, parallelOptions, relativePath =>
        {
            var leftFullPath = left[relativePath];
            var rightFullPath = right[relativePath];

            var leftLines = NormalizedLines(leftFullPath, options);
            var rightLines = NormalizedLines(rightFullPath, options);

            if (leftLines.SequenceEqual(rightLines))
            {
                unchanged.Add(relativePath);
            }
            else
            {
                changed.Add(relativePath);
                if (options.ShowContext)
                    fileDiffs.Add(new FileDiff
                    {
                        Path = relativePath,
                        Lines = LineDiffer.Diff(leftLines, rightLines)
                    });
            }
        });

        return new SchemaDiffResult
        {
            Added = Sorted(added),
            Removed = Sorted(removed),
            Changed = Sorted(changed),
            Unchanged = Sorted(unchanged),
            Statistics = BuildStatistics(added, removed, changed),
            FileDiffs = fileDiffs.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static IReadOnlyList<DiffTypeStat> BuildStatistics(
        IEnumerable<string> added,
        IEnumerable<string> removed,
        IEnumerable<string> changed)
    {
        var types = new SortedDictionary<string, (int Added, int Removed, int Changed)>(StringComparer.OrdinalIgnoreCase);

        void Tally(IEnumerable<string> paths, char kind)
        {
            foreach (var path in paths)
            {
                var type = ObjectType(path);
                types.TryGetValue(type, out var counts);
                counts = kind switch
                {
                    'a' => (counts.Added + 1, counts.Removed, counts.Changed),
                    'r' => (counts.Added, counts.Removed + 1, counts.Changed),
                    _ => (counts.Added, counts.Removed, counts.Changed + 1)
                };
                types[type] = counts;
            }
        }

        Tally(added, 'a');
        Tally(removed, 'r');
        Tally(changed, 'c');

        return types
            .Select(kv => new DiffTypeStat
            {
                ObjectType = kv.Key,
                Added = kv.Value.Added,
                Removed = kv.Value.Removed,
                Changed = kv.Value.Changed
            })
            .ToList();
    }

    private static string ObjectType(string relativePath)
    {
        var index = relativePath.IndexOf('/');
        return index > 0 ? relativePath[..index] : "(root)";
    }

    private static ExportOptions BuildExportOptions(SchemaDiffOptions diffOptions)
    {
        return new ExportOptions
        {
            Schemas = diffOptions.Schemas is { Length: > 0 } ? diffOptions.Schemas : ["public"],
            ExcludeSchemas = diffOptions.ExcludeSchemas ?? ["pg_catalog", "information_schema"],
            Parallel = diffOptions.Parallel,
            Include = new IncludeOptions
            {
                Schemas = true,
                Extensions = true,
                Types = true,
                Sequences = true,
                Domains = true,
                ForeignTables = true,
                Tables = true,
                Constraints = true,
                Indexes = true,
                Views = true,
                Triggers = true,
                EventTriggers = true,
                Rules = true,
                Aggregates = true,
                Operators = true,
                Casts = true,
                Publications = true,
                Subscriptions = true,
                Policies = true,
                Comments = true,
                Grants = true,
                Functions = true
            },
            Format = new FormatOptions
            {
                UseIfNotExists = true,
                SplitConstraints = true,
                SplitIndexes = true
            }
        };
    }

    private static Dictionary<string, string> Enumerate(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            // Skip generated deployment artifacts that are not part of the schema state.
            if (relative.Equals("deploy.sql", StringComparison.OrdinalIgnoreCase))
                continue;

            map[relative] = file;
        }

        return map;
    }

    /// <summary>
    /// Reads a file and returns its lines after applying the requested
    /// normalization: line endings are always unified, and comments/whitespace
    /// are optionally stripped so cosmetic edits don't register as changes.
    /// </summary>
    private static string[] NormalizedLines(string path, SchemaDiffOptions options)
    {
        var text = File.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .TrimEnd('\n');

        var lines = text.Split('\n');
        var result = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var line = raw;

            if (options.IgnoreComments)
                line = StripComment(line);

            if (options.IgnoreWhitespace)
            {
                line = CollapseWhitespace(line);
                if (line.Length == 0)
                    continue;
            }
            else if (options.IgnoreComments && line.Length == 0 && raw.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                // Drop lines that became empty solely because they were whole-line comments.
                continue;
            }

            result.Add(line);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Best-effort removal of a trailing <c>--</c> line comment. Ignores <c>--</c>
    /// occurrences inside single-quoted string literals.
    /// </summary>
    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '\'')
                inString = !inString;
            else if (!inString && c == '-' && line[i + 1] == '-')
                return line[..i].TrimEnd();
        }

        return line;
    }

    private static string CollapseWhitespace(string line)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        var lastWasSpace = false;

        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                lastWasSpace = true;
                continue;
            }

            if (lastWasSpace && sb.Length > 0)
                sb.Append(' ');

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> items)
    {
        var list = items.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }
}
