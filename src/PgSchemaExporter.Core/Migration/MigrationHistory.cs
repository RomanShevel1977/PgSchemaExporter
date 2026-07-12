using System.Text.Json;
using System.Text.Json.Serialization;

namespace PgSchemaExporter.Core.Migration;

/// <summary>
/// Tracks generated migrations in a JSON history file (<c>migrations/history.json</c>)
/// so teams have an auditable record of every migration produced by the tool:
/// when it was generated, its name, statement counts, and whether it was destructive.
/// </summary>
public static class MigrationHistory
{
    public const string DefaultFileName = "history.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<MigrationHistoryFile> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return new MigrationHistoryFile();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return new MigrationHistoryFile();

        return JsonSerializer.Deserialize<MigrationHistoryFile>(json, SerializerOptions)
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

        var history = await ReadAsync(path, cancellationToken);
        var entries = history.Migrations.ToList();
        entries.Add(entry);

        var updated = new MigrationHistoryFile { Migrations = entries };
        var json = JsonSerializer.Serialize(updated, SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
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
