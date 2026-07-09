namespace PgSchemaExporter.Core.Diff;

/// <summary>
/// Produces a line-by-line diff of two text blocks using the classic
/// longest-common-subsequence algorithm. The output preserves unchanged
/// (context) lines interleaved with added/removed lines so callers can render
/// a readable, git-style change view.
/// </summary>
public static class LineDiffer
{
    public static IReadOnlyList<DiffLine> Diff(string[] left, string[] right)
    {
        var n = left.Length;
        var m = right.Length;

        // lcs[i, j] = length of the LCS of left[i..] and right[j..].
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = left[i] == right[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        int x = 0, y = 0;

        while (x < n && y < m)
        {
            if (left[x] == right[y])
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Context, Text = left[x] });
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[x] });
                x++;
            }
            else
            {
                result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[y] });
                y++;
            }
        }

        while (x < n)
            result.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[x++] });

        while (y < m)
            result.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[y++] });

        return result;
    }
}
