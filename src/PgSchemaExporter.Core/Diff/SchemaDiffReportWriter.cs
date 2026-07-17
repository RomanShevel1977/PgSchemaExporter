using System.Text;
using System.Text.Json;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Serialization;

namespace PgSchemaExporter.Core.Diff;

public sealed class SchemaDiffReportDto
{
    public IReadOnlyList<string> Added { get; init; } = [];
    public IReadOnlyList<string> Removed { get; init; } = [];
    public IReadOnlyList<string> Changed { get; init; } = [];
    public IReadOnlyList<string> Unchanged { get; init; } = [];
    public bool HasDifferences { get; init; }
    public IReadOnlyList<SchemaDiffStatDto> Statistics { get; init; } = [];
    public IReadOnlyList<SchemaFileDiffDto> FileDiffs { get; init; } = [];
}

public sealed class SchemaDiffStatDto
{
    public string ObjectType { get; init; } = "";
    public int Added { get; init; }
    public int Removed { get; init; }
    public int Changed { get; init; }
    public int Total { get; init; }
}

public sealed class SchemaFileDiffDto
{
    public string Path { get; init; } = "";
    public IReadOnlyList<SchemaDiffLineDto> Lines { get; init; } = [];
}

public sealed class SchemaDiffLineDto
{
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
}

public sealed class SchemaDiffReportWriter
{
    public string BuildReport(SchemaDiffResult result, DiffFormat format = DiffFormat.Text)
    {
        return format switch
        {
            DiffFormat.Json => BuildJsonReport(result),
            DiffFormat.Html => BuildHtmlReport(result),
            _ => BuildTextReport(result)
        };
    }

    public async Task WriteAsync(string outputFile, SchemaDiffResult result, DiffFormat format = DiffFormat.Text, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFile));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputFile, BuildReport(result, format), cancellationToken);
    }

    private static string BuildTextReport(SchemaDiffResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Schema diff report");
        sb.AppendLine();
        sb.AppendLine($"- Added:     {result.Added.Count}");
        sb.AppendLine($"- Removed:   {result.Removed.Count}");
        sb.AppendLine($"- Changed:   {result.Changed.Count}");
        sb.AppendLine($"- Unchanged: {result.Unchanged.Count}");
        sb.AppendLine();

        if (result.Statistics.Count > 0)
        {
            sb.AppendLine("## Changes by type");
            foreach (var stat in result.Statistics)
                sb.AppendLine($"- {stat.ObjectType}: +{stat.Added} -{stat.Removed} ~{stat.Changed}");
            sb.AppendLine();
        }

        AppendSection(sb, "Added", result.Added, "+");
        AppendSection(sb, "Removed", result.Removed, "-");
        AppendSection(sb, "Changed", result.Changed, "~");

        if (result.FileDiffs.Count > 0)
        {
            sb.AppendLine("## Details");
            sb.AppendLine();
            foreach (var file in result.FileDiffs)
            {
                sb.AppendLine($"### {file.Path}");
                foreach (var line in file.Lines)
                {
                    var marker = line.Kind switch
                    {
                        DiffLineKind.Added => "+",
                        DiffLineKind.Removed => "-",
                        _ => " "
                    };
                    sb.AppendLine($"{marker} {line.Text}");
                }
                sb.AppendLine();
            }
        }

        if (!result.HasDifferences)
            sb.AppendLine("No differences detected.");

        return sb.ToString();
    }

    private static string BuildJsonReport(SchemaDiffResult result)
    {
        var dto = new SchemaDiffReportDto
        {
            Added = result.Added,
            Removed = result.Removed,
            Changed = result.Changed,
            Unchanged = result.Unchanged,
            HasDifferences = result.HasDifferences,
            Statistics = result.Statistics.Select(s => new SchemaDiffStatDto
            {
                ObjectType = s.ObjectType,
                Added = s.Added,
                Removed = s.Removed,
                Changed = s.Changed,
                Total = s.Total
            }).ToList(),
            FileDiffs = result.FileDiffs.Select(f => new SchemaFileDiffDto
            {
                Path = f.Path,
                Lines = f.Lines.Select(l => new SchemaDiffLineDto
                {
                    Kind = l.Kind.ToString().ToLowerInvariant(),
                    Text = l.Text
                }).ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(dto, PgSchemaExporterJsonContext.Default.SchemaDiffReportDto);
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<string> items, string marker)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine($"## {title} ({items.Count})");
        foreach (var item in items)
            sb.AppendLine($"{marker} {item}");
        sb.AppendLine();
    }

    private static string BuildHtmlReport(SchemaDiffResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Schema diff report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(":root{--added:#1a7f37;--removed:#cf222e;--changed:#9a6700;--bg:#0d1117;--panel:#161b22;--fg:#e6edf3;--muted:#8b949e;--border:#30363d}");
        sb.AppendLine("*{box-sizing:border-box}body{margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;background:var(--bg);color:var(--fg);line-height:1.5}");
        sb.AppendLine(".wrap{max-width:960px;margin:0 auto;padding:2rem 1.5rem}");
        sb.AppendLine("h1{font-size:1.6rem;margin:0 0 1.25rem}");
        sb.AppendLine(".summary{display:flex;gap:.75rem;flex-wrap:wrap;margin-bottom:2rem}");
        sb.AppendLine(".pill{padding:.5rem .9rem;border-radius:999px;background:var(--panel);border:1px solid var(--border);font-size:.9rem}");
        sb.AppendLine(".pill b{font-weight:700}");
        sb.AppendLine(".pill.added b{color:var(--added)}.pill.removed b{color:var(--removed)}.pill.changed b{color:var(--changed)}");
        sb.AppendLine("section{background:var(--panel);border:1px solid var(--border);border-radius:8px;margin-bottom:1.25rem;overflow:hidden}");
        sb.AppendLine("section>h2{margin:0;padding:.75rem 1rem;font-size:1rem;border-bottom:1px solid var(--border)}");
        sb.AppendLine("ul{list-style:none;margin:0;padding:0}");
        sb.AppendLine("li{padding:.4rem 1rem;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:.85rem;border-bottom:1px solid var(--border)}");
        sb.AppendLine("li:last-child{border-bottom:none}");
        sb.AppendLine("li.added{border-left:3px solid var(--added)}li.removed{border-left:3px solid var(--removed)}li.changed{border-left:3px solid var(--changed)}");
        sb.AppendLine(".marker{display:inline-block;width:1.1rem;font-weight:700}");
        sb.AppendLine(".marker.added{color:var(--added)}.marker.removed{color:var(--removed)}.marker.changed{color:var(--changed)}");
        sb.AppendLine(".none{color:var(--muted);padding:1rem;text-align:center}");
        sb.AppendLine("footer{color:var(--muted);font-size:.8rem;margin-top:2rem;text-align:center}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body><div class=\"wrap\">");
        sb.AppendLine("<h1>Schema diff report</h1>");

        sb.AppendLine("<div class=\"summary\">");
        sb.AppendLine($"<span class=\"pill added\">Added <b>{result.Added.Count}</b></span>");
        sb.AppendLine($"<span class=\"pill removed\">Removed <b>{result.Removed.Count}</b></span>");
        sb.AppendLine($"<span class=\"pill changed\">Changed <b>{result.Changed.Count}</b></span>");
        sb.AppendLine($"<span class=\"pill\">Unchanged <b>{result.Unchanged.Count}</b></span>");
        sb.AppendLine("</div>");

        if (result.Statistics.Count > 0)
        {
            sb.AppendLine("<section>");
            sb.AppendLine("<h2>Changes by type</h2>");
            sb.AppendLine("<ul>");
            foreach (var stat in result.Statistics)
                sb.AppendLine($"<li>{HtmlEncode(stat.ObjectType)}: <span class=\"marker added\">+{stat.Added}</span> <span class=\"marker removed\">-{stat.Removed}</span> <span class=\"marker changed\">~{stat.Changed}</span></li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("</section>");
        }

        AppendHtmlSection(sb, "Added", result.Added, "added", "+");
        AppendHtmlSection(sb, "Removed", result.Removed, "removed", "-");
        AppendHtmlSection(sb, "Changed", result.Changed, "changed", "~");

        foreach (var file in result.FileDiffs)
        {
            sb.AppendLine("<section>");
            sb.AppendLine($"<h2>{HtmlEncode(file.Path)}</h2>");
            sb.AppendLine("<ul>");
            foreach (var line in file.Lines)
            {
                var cssClass = line.Kind switch
                {
                    DiffLineKind.Added => "added",
                    DiffLineKind.Removed => "removed",
                    _ => ""
                };
                var marker = line.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    _ => " "
                };
                sb.AppendLine($"<li class=\"{cssClass}\"><span class=\"marker {cssClass}\">{marker}</span>{HtmlEncode(line.Text)}</li>");
            }
            sb.AppendLine("</ul>");
            sb.AppendLine("</section>");
        }

        if (!result.HasDifferences)
            sb.AppendLine("<p class=\"none\">No differences detected.</p>");

        sb.AppendLine("<footer>Generated by pgschema-export</footer>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendHtmlSection(StringBuilder sb, string title, IReadOnlyList<string> items, string cssClass, string marker)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine("<section>");
        sb.AppendLine($"<h2>{title} ({items.Count})</h2>");
        sb.AppendLine("<ul>");
        foreach (var item in items)
            sb.AppendLine($"<li class=\"{cssClass}\"><span class=\"marker {cssClass}\">{marker}</span>{HtmlEncode(item)}</li>");
        sb.AppendLine("</ul>");
        sb.AppendLine("</section>");
    }

    private static string HtmlEncode(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.Length == value.Length ? value : sb.ToString();
    }
}
