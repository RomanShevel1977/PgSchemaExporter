using System.Text;
using System.Text.Json;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Tracks generated migrations in a JSON history file (<c>migrations/history.json</c>)
/// so teams have an auditable record of every migration produced by the tool:
/// when it was generated, its name, statement counts, and whether it was destructive.
/// </summary>
public static class MigrationHistory
{
    public const string DefaultFileName = "history.json";

    public static async Task<MigrationHistoryFile> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return new MigrationHistoryFile();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return new MigrationHistoryFile();

        return JsonSerializer.Deserialize(json, Serialization.PgSchemaExporterJsonContext.Default.MigrationHistoryFile)
            ?? new MigrationHistoryFile();
    }

    /// <summary>
    /// Appends <paramref name="entry"/> to the history file located in
    /// <paramref name="outputDirectory"/>, creating the file if needed.
    /// </summary>
    public static async Task AppendAsync(
        string outputDirectory,
        MigrationHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, DefaultFileName);

        await using var stream = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
        {
            var json = await reader.ReadToEndAsync(cancellationToken);
            var history = string.IsNullOrWhiteSpace(json)
                ? new MigrationHistoryFile()
                : JsonSerializer.Deserialize(json, Serialization.PgSchemaExporterJsonContext.Default.MigrationHistoryFile)
                    ?? new MigrationHistoryFile();

            var entries = history.Migrations.ToList();
            entries.Add(entry);
            var updated = new MigrationHistoryFile { Migrations = entries };
            var newJson = JsonSerializer.Serialize(updated, Serialization.PgSchemaExporterJsonContext.Default.MigrationHistoryFile);

            stream.Position = 0;
            stream.SetLength(0);

            await using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                await writer.WriteAsync(newJson);
                await writer.FlushAsync();
            }
        }
    }
}

public sealed class MigrationHistoryFile
{
    public IReadOnlyList<MigrationHistoryEntry> Migrations { get; init; } = [];
}

public sealed class MigrationHistoryEntry
{
    public DateTimeOffset AppliedAt { get; init; }
    public string? Name { get; init; }
    public string UpFile { get; init; } = "";
    public string DownFile { get; init; } = "";
    public int UpStatements { get; init; }
    public int DownStatements { get; init; }
    public bool Destructive { get; init; }
}
