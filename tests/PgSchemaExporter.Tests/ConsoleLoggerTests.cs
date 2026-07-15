using System.Text;
using Microsoft.Extensions.Logging;
using PgSchemaExporter.Cli.Diagnostics;
using PgSchemaExporter.Core.Diagnostics;
using Xunit;

namespace PgSchemaExporter.Tests;

public class ConsoleLoggerTests
{
    private static StringWriter Capture(Action action)
    {
        var original = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer;
    }

    [Fact]
    public void Log_Quiet_OnlyWritesWarningAndAbove()
    {
        var logger = new ConsoleLogger(Verbosity.Quiet);

        var sb = Capture(() =>
        {
            logger.Log(LogLevel.Information, new EventId(1), "info", null, (s, _) => s!);
            logger.Log(LogLevel.Warning, new EventId(2), "warn", null, (s, _) => s!);
        });

        var output = sb.ToString();
        Assert.DoesNotContain("info", output);
        Assert.Contains("warn", output);
    }

    [Fact]
    public void Log_Verbose_WritesDebug()
    {
        var logger = new ConsoleLogger(Verbosity.Verbose);

        var sb = Capture(() => logger.Log(LogLevel.Debug, new EventId(1), "debug", null, (s, _) => s!));

        Assert.Contains("debug", sb.ToString());
    }

    [Fact]
    public void Log_Exception_AppendsExceptionToError()
    {
        var logger = new ConsoleLogger(Verbosity.Normal);

        var sb = Capture(() => logger.Log(LogLevel.Error, new EventId(1), "failed", new InvalidOperationException("boom"), (s, _) => s!));

        var output = sb.ToString();
        Assert.Contains("failed", output);
        Assert.Contains("boom", output);
    }

    [Fact]
    public void Log_None_IsIgnored()
    {
        var logger = new ConsoleLogger(Verbosity.Normal);

        var sb = Capture(() => logger.Log(LogLevel.None, new EventId(1), "none", null, (s, _) => s!));

        Assert.Empty(sb.ToString());
    }

    [Fact]
    public void IsEnabled_RespectsVerbosity()
    {
        var logger = new ConsoleLogger(Verbosity.Quiet);

        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.Information));

        logger = new ConsoleLogger(Verbosity.Verbose);
        Assert.True(logger.IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void BeginScope_ReturnsNull()
    {
        var logger = new ConsoleLogger(Verbosity.Normal);

        var scope = logger.BeginScope("state");

        Assert.Null(scope);
    }
}
