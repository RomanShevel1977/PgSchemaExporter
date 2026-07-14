using System.Text;

namespace PgSchemaExporter.Core.Diagramming;

/// <summary>
/// Renders an <see cref="ErModel"/> as Graphviz DOT using HTML-like table nodes.
/// Produce an image with e.g. <c>dot -Tsvg schema.dot -o schema.svg</c>.
/// </summary>
public static class DotErRenderer
{
    public static string Render(ErModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph schema {");
        sb.AppendLine("    rankdir=LR;");
        sb.AppendLine("    graph [splines=true, nodesep=0.6, ranksep=1.0];");
        sb.AppendLine("    node [shape=plaintext, fontname=\"Helvetica\", fontsize=10];");
        sb.AppendLine("    edge [fontname=\"Helvetica\", fontsize=9];");
        sb.AppendLine();

        foreach (var table in model.Tables)
        {
            var nodeId = Quote(table.QualifiedName);
            sb.AppendLine($"    {nodeId} [label=<");
            sb.AppendLine("        <table border=\"1\" cellborder=\"0\" cellspacing=\"0\" cellpadding=\"4\">");
            sb.AppendLine($"            <tr><td bgcolor=\"#4472c4\"><font color=\"white\"><b>{Escape(table.QualifiedName)}</b></font></td></tr>");

            foreach (var column in table.Columns)
                sb.AppendLine($"            <tr><td align=\"left\">{FormatColumn(column)}</td></tr>");

            sb.AppendLine("        </table>>];");
        }

        sb.AppendLine();

        foreach (var rel in model.Relationships)
        {
            var label = rel.FromColumns.Count > 0 ? string.Join(", ", rel.FromColumns) : "fk";
            var style = rel.IsMandatory ? "solid" : "dashed";

            // Child (referencing) points to parent (referenced).
            sb.AppendLine(
                $"    {Quote(rel.FromTable)} -> {Quote(rel.ToTable)} " +
                $"[label=\"{Escape(label)}\", style={style}, arrowhead=crow];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string FormatColumn(ErColumn column)
    {
        var markers = new List<string>(3);
        if (column.IsPrimaryKey) markers.Add("PK");
        if (column.IsForeignKey) markers.Add("FK");
        if (column.IsUnique && !column.IsPrimaryKey) markers.Add("UK");

        var keyPrefix = markers.Count > 0 ? $"[{string.Join(",", markers)}] " : string.Empty;
        var nullable = column.IsNullable ? string.Empty : " <b>*</b>";
        var name = Escape(column.Name);
        var type = Escape(column.DataType);

        var display = $"{keyPrefix}{name}{nullable} : {type}";
        return column.IsPrimaryKey ? $"<b>{display}</b>" : display;
    }

    private static string Quote(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    /// <summary>Escapes text for use inside a Graphviz HTML-like label.</summary>
    private static string Escape(string value)
        => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}
