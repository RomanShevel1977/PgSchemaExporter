using Microsoft.Extensions.Logging;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Metadata;

namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Resolves and dispatches CLI commands by name.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly IMetadataProvider _metadataProvider;
    private readonly Dictionary<string, ICommand> _commands;

    public CommandDispatcher(IMetadataProvider metadataProvider)
    {
        _metadataProvider = metadataProvider;
        _commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);

        Register(new DiffCommand());
        Register(new ExportCommand());
        Register(new InitCommand());
        Register(new ApplyCommand());
        Register(new DiagramCommand());
        Register(new DriftCommand());
        Register(new FingerprintCommand());
        Register(new MigrateCommand());
        Register(new PlanCommand());
        Register(new SplitDumpCommand());
        Register(new WatchCommand());
    }

    public IReadOnlyCollection<string> KnownCommands => _commands.Keys;

    /// <summary>
    /// Executes a registered command and returns its exit code, or <c>null</c> if the command is unknown.
    /// </summary>
    public async Task<int?> ExecuteAsync(
        string name,
        string[] args,
        IProgressReporter progress,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!_commands.TryGetValue(name, out var command))
            return null;

        var context = new CommandContext
        {
            Args = args,
            Progress = progress,
            Logger = logger,
            MetadataProvider = _metadataProvider,
            CancellationToken = cancellationToken
        };

        return await command.ExecuteAsync(context);
    }

    private void Register(ICommand command)
    {
        _commands[command.Name] = command;
    }
}
