using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Diff;

/// <summary>
/// Compares two exported schema directories (the git-native layout) and reports
/// which object files were added, removed, or changed between them.
/// </summary>
public sealed class SchemaDiffer
{
    public SchemaDiffResult Diff(SchemaDiffOptions options)
    {
        options.EnsureValid();

        var left = Enumerate(options.LeftDirectory);
        var right = Enumerate(options.RightDirectory);

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
