using System.Text;

namespace PgSchemaExporter.Core.Diagramming;

/// <summary>
/// Renders an <see cref="ErModel"/> as Mermaid <c>erDiagram</c> syntax, which
/// renders inline in GitHub/GitLab Markdown and most wikis.
/// </summary>
public static class MermaidErRenderer
{
    public static string Render(ErModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        // Map each table's qualified name to a Mermaid-safe entity id.
        var entityIds = BuildEntityIds(model);

        foreach (var table in model.Tables)
        {
            var id = entityIds[table.QualifiedName];
            sb.AppendLine($"    {id} {{");

            foreach (var column in table.Columns)
            {
                var type = SanitizeToken(column.DataType);
                if (type.Length == 0)
                    type = "unknown";

                var name = SanitizeToken(column.Name);
                var keys = BuildKeyMarker(column);
                var comment = column.IsNullable ? " \"nullable\"" : string.Empty;

                var keyPart = keys.Length > 0 ? $" {keys}" : string.Empty;
                sb.AppendLine($"        {type} {name}{keyPart}{comment}");
            }

            sb.AppendLine("    }");
        }

        foreach (var rel in model.Relationships)
        {
            if (!entityIds.TryGetValue(rel.FromTable, out var childId) ||
                !entityIds.TryGetValue(rel.ToTable, out var parentId))
                continue;

            // Parent (referenced) on the left, child (referencing) on the right.
            // Mandatory (NOT NULL) FK => parent is "exactly one"; otherwise "zero or one".
            var cardinality = rel.IsMandatory ? "||--o{" : "|o--o{";
            var label = rel.FromColumns.Count > 0 ? string.Join(",", rel.FromColumns) : "fk";

            sb.AppendLine($"    {parentId} {cardinality} {childId} : \"{EscapeLabel(label)}\"");
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static Dictionary<string, string> BuildEntityIds(ErModel model)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in model.Tables)
        {
            // Public schema is elided for readability; other schemas are prefixed.
            var display = string.IsNullOrEmpty(table.Schema) || table.Schema == "public"
                ? table.Name
                : $"{table.Schema}_{table.Name}";

            var id = SanitizeToken(display);
            if (id.Length == 0)
                id = "table";

            var candidate = id;
            var n = 2;
            while (!used.Add(candidate))
                candidate = $"{id}_{n++}";

            ids[table.QualifiedName] = candidate;
        }

        return ids;
    }

    private static string BuildKeyMarker(ErColumn column)
    {
        var markers = new List<string>(3);
        if (column.IsPrimaryKey) markers.Add("PK");
        if (column.IsForeignKey) markers.Add("FK");
        if (column.IsUnique && !column.IsPrimaryKey) markers.Add("UK");
        return string.Join(",", markers);
    }

    /// <summary>
    /// Reduces an identifier or type to a Mermaid-safe token: only letters, digits,
    /// and underscores survive; runs of other characters collapse to a single
    /// underscore, and leading/trailing underscores are trimmed.
    /// </summary>
    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastUnderscore = false;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }

        return sb.ToString().Trim('_');
    }

    private static string EscapeLabel(string label)
        => label.Replace("\"", "'");
}
