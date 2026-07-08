using System.Net.Sockets;
using Npgsql;

namespace PgSchemaExporter.Core.Diagnostics;

/// <summary>
/// Turns raw exceptions into concise, actionable messages with a suggested fix.
/// Keeps the CLI free of database-specific error handling logic.
/// </summary>
public static class FriendlyError
{
    /// <summary>
    /// Describes an exception as a short headline plus an optional suggestion.
    /// </summary>
    public static (string Message, string? Suggestion) Describe(Exception ex)
    {
        return ex switch
        {
            ArgumentException => (ex.Message,
                "Run 'pgschema-export --help' to review the expected arguments."),

            FileNotFoundException fnf => (fnf.Message,
                $"Check that the file exists: {fnf.FileName ?? "(path not reported)"}"),

            DirectoryNotFoundException => (ex.Message,
                "Check that the directory path is correct and was created by a prior export."),

            PostgresException pg => DescribePostgres(pg),

            NpgsqlException => (ex.Message, DescribeNpgsql(ex)),

            SocketException => (ex.Message,
                "The database host is unreachable. Verify Host/Port and that PostgreSQL is running and accepting TCP connections."),

            _ => (ex.Message, null)
        };
    }

    private static (string, string?) DescribePostgres(PostgresException pg)
    {
        // See https://www.postgresql.org/docs/current/errcodes-appendix.html
        var suggestion = pg.SqlState switch
        {
            "28P01" or "28000" => "Authentication failed. Check the Username/Password in your connection string.",
            "3D000" => "The database does not exist. Check the 'Database' value in your connection string.",
            "42501" => "The role lacks privileges to read the catalog. Grant it CONNECT and USAGE, or use a superuser for export.",
            "53300" => "Too many connections. Reduce --parallel concurrency or increase the server's max_connections.",
            _ => null
        };

        return ($"PostgreSQL error {pg.SqlState}: {pg.MessageText}", suggestion);
    }

    private static string DescribeNpgsql(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("timeout"))
            return "The connection timed out. Verify the host/port and that the server is reachable, or increase the Timeout setting.";

        if (message.Contains("password"))
            return "A password may be required. Add 'Password=...' to your connection string.";

        return "Verify the connection string. Expected format: 'Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=secret'.";
    }
}
