using System.Buffers;

namespace PgSchemaExporter.Core.Diff;

/// <summary>
/// Produces a line-by-line diff of two text blocks using the classic
/// longest-common-subsequence algorithm. The output preserves unchanged
/// (context) lines interleaved with added/removed lines so callers can render
/// a readable, git-style change view.
/// </summary>
public static class LineDiffer
{
    // Direction arrows stored for backtracking.
    // 0 = diagonal (context), 1 = down (removed from left), 2 = right (added from right).
    private const byte Diagonal = 0;
    private const byte Down = 1;
    private const byte Right = 2;

    public static IReadOnlyList<DiffLine> Diff(string[] left, string[] right)
    {
        var n = left.Length;
        var m = right.Length;

        if (n == 0 && m == 0)
            return Array.Empty<DiffLine>();

        // Fast path: completely disjoint or fully equal arrays.
        if (n == 0)
        {
            var allAdded = new List<DiffLine>(m);
            for (var i = 0; i < m; i++)
                allAdded.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[i] });
            return allAdded;
        }

        if (m == 0)
        {
            var allRemoved = new List<DiffLine>(n);
            for (var i = 0; i < n; i++)
                allRemoved.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[i] });
            return allRemoved;
        }

        var result = new List<DiffLine>(n + m);

        // Only one row of LCS values plus a previous row is needed for the fill.
        var previous = ArrayPool<int>.Shared.Rent(m + 1);
        var current = ArrayPool<int>.Shared.Rent(m + 1);
        Array.Fill(previous, 0, 0, m + 1);

        // Directions are one byte per cell instead of four bytes for the LCS matrix.
        var directionsLength = n * m;
        var directions = directionsLength > 0
            ? ArrayPool<byte>.Shared.Rent(directionsLength)
            : Array.Empty<byte>();

        try
        {
            for (var i = n - 1; i >= 0; i--)
            {
                current[m] = 0;

                for (var j = m - 1; j >= 0; j--)
                {
                    if (left[i] == right[j])
                    {
                        current[j] = previous[j + 1] + 1;
                        directions[i * m + j] = Diagonal;
                    }
                    else if (previous[j] >= current[j + 1])
                    {
                        current[j] = previous[j];
                        directions[i * m + j] = Down;
                    }
                    else
                    {
                        current[j] = current[j + 1];
                        directions[i * m + j] = Right;
                    }
                }

                // Swap rows for the next iteration.
                (previous, current) = (current, previous);
            }

            // Backtrack from (0, 0) using the stored directions.
            var x = 0;
            var y = 0;

            while (x < n && y < m)
            {
                var dir = directions[x * m + y];
                if (dir == Diagonal)
                {
                    result.Add(new DiffLine { Kind = DiffLineKind.Context, Text = left[x] });
                    x++;
                    y++;
                }
                else if (dir == Down)
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
        finally
        {
            ArrayPool<int>.Shared.Return(previous);
            ArrayPool<int>.Shared.Return(current);
            if (directionsLength > 0)
                ArrayPool<byte>.Shared.Return(directions);
        }
    }
}
