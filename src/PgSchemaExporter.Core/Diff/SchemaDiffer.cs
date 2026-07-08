using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
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
    private readonly LiveSchemaExporter _liveExporter = new();

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
            return DiffDirectories(leftDir, rightDir);
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

        return DiffDirectories(options.LeftDirectory, options.RightDirectory);
    }

    private static SchemaDiffResult DiffDirectories(string leftDir, string rightDir)
    {
        var left = Enumerate(leftDir);
        var right = Enumerate(rightDir);

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        var unchanged = new List<string>();

        foreach (var (relativePath, rightFullPath) in right)
        {
            if (!left.TryGetValue(relativePath, out var leftFullPath))
            {
                added.Add(relativePath);
                continue;
            }

            if (ReadNormalized(leftFullPath) == ReadNormalized(rightFullPath))
                unchanged.Add(relativePath);
            else
                changed.Add(relativePath);
        }

        foreach (var relativePath in left.Keys)
        {
            if (!right.ContainsKey(relativePath))
                removed.Add(relativePath);
        }

        return new SchemaDiffResult
        {
            Added = Sorted(added),
            Removed = Sorted(removed),
            Changed = Sorted(changed),
            Unchanged = Sorted(unchanged)
        };
    }

    private static ExportOptions BuildExportOptions(SchemaDiffOptions diffOptions)
    {
        return new ExportOptions
        {
            Schemas = ["public"],
            ExcludeSchemas = ["pg_catalog", "information_schema"],
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
            map[relative] = file;
        }

        return map;
    }

    private static string ReadNormalized(string path)
    {
        var text = File.ReadAllText(path);
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
    }

    private static IReadOnlyList<string> Sorted(List<string> items)
    {
        items.Sort(StringComparer.OrdinalIgnoreCase);
        return items;
    }
}
