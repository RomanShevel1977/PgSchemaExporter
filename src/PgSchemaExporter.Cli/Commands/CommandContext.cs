using Microsoft.Extensions.Logging;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Shared context passed to every command handler.
/// </summary>
public sealed class CommandContext
{
    public required string[] Args { get; init; }
    public required IProgressReporter Progress { get; init; }
    public required ILogger Logger { get; init; }
    public required IMetadataProvider MetadataProvider { get; init; }
    public CancellationToken CancellationToken { get; init; } = default;
}
