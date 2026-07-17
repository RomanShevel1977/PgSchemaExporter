namespace PgSchemaExporter.Cli.Commands;

/// <summary>
/// Represents a CLI command that can be executed with parsed arguments and shared services.
/// </summary>
public interface ICommand
{
    /// <summary>The command name as typed on the CLI (e.g. "export").</summary>
    string Name { get; }

    /// <summary>Execute the command and return the process exit code.</summary>
    Task<int> ExecuteAsync(CommandContext context);
}
