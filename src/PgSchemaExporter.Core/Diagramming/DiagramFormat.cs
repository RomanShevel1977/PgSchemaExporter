namespace PgSchemaExporter.Core.Diagramming;

/// <summary>Output format for a rendered ER diagram.</summary>
public enum DiagramFormat
{
    /// <summary>Mermaid <c>erDiagram</c> syntax (renders on GitHub, GitLab, many wikis).</summary>
    Mermaid,

    /// <summary>Graphviz DOT (render to SVG/PNG with <c>dot</c>).</summary>
    Dot
}
