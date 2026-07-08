using Microsoft.Extensions.Logging;
using PgSchemaExporter.Core.Diagnostics;

namespace PgSchemaExporter.Cli.Diagnostics;

/// <summary>
/// A minimal <see cref="ILogger"/> for the CLI. Diagnostic messages go to stderr so
/// they never pollute machine-readable output (e.g. JSON diffs) on stdout. The
/// minimum level is derived from the chosen <see cref="Verbosity"/>.
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    private readonly LogLevel _minLevel;

    public ConsoleLogger(Verbosity verbosity)
    {
        _minLevel = verbosity switch
        {
            Verbosity.Quiet => LogLevel.Warning,
            Verbosity.Verbose => LogLevel.Debug,
            _ => LogLevel.Information
        };
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
            return;

        var label = logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "debug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "error",
            LogLevel.Critical => "crit",
            _ => "log"
        };

        Console.Error.WriteLine($"[{label}] {message}");
        if (exception is not null)
            Console.Error.WriteLine($"[{label}] {exception}");
    }
}
