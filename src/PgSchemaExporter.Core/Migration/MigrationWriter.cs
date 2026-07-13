using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Writes a generated <see cref="MigrationScript"/> to timestamped <c>.up.sql</c> and
/// <c>.down.sql</c> files in the output directory.
/// </summary>
public sealed class MigrationWriter
{
    public sealed class WriteResult
    {
        public required string UpFile { get; init; }
        public required string DownFile { get; init; }
    }

    public async Task<WriteResult> WriteAsync(
        MigrationOptions options,
        MigrationScript script,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var stamp = timestamp.ToString("yyyyMMddHHmmss");
        var slug = BuildSlug(options.Name);
        var baseName = string.IsNullOrEmpty(slug) ? stamp : $"{stamp}_{slug}";

        var upPath = Path.Combine(options.OutputDirectory, $"{baseName}.up.sql");
        var downPath = Path.Combine(options.OutputDirectory, $"{baseName}.down.sql");

        var renderOptions = options.ToRenderOptions();
        await File.WriteAllTextAsync(upPath, script.RenderUp(renderOptions), cancellationToken);
        await File.WriteAllTextAsync(downPath, script.RenderDown(renderOptions), cancellationToken);

        return new WriteResult { UpFile = upPath, DownFile = downPath };
    }

    private static string BuildSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();

        var slug = new string(chars).Trim('_');
        while (slug.Contains("__"))
            slug = slug.Replace("__", "_");

        return SqlIdentifier.SafeFileName(slug);
    }
}
