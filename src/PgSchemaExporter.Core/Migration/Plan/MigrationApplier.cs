using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Migration.Plan;

/// <summary>
/// Executes a reviewed <see cref="MigrationPlan"/> against a live PostgreSQL
/// database. Transactional statements run inside a single transaction; statements
/// flagged to run outside a transaction (e.g. concurrent index builds) are
/// executed separately with autocommit.
/// </summary>
public sealed class MigrationApplier
{
    public sealed class ApplyResult
    {
        public int Executed { get; init; }
        public int Skipped { get; init; }
        public bool DryRun { get; init; }
    }

    public sealed class ApplyOptions
    {
        public required string ConnectionString { get; init; }

        /// <summary>Apply the down direction instead of up (rollback).</summary>
        public bool Rollback { get; init; }

        /// <summary>Print statements without executing them.</summary>
        public bool DryRun { get; init; }

        /// <summary>Maximum seconds to wait for a single statement before cancelling it. Zero disables the timeout.</summary>
        public int CommandTimeoutSeconds { get; init; }
    }

    public async Task<ApplyResult> ApplyAsync(
        MigrationPlan plan,
        ApplyOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= NullProgressReporter.Instance;
        logger ??= NullLogger.Instance;

        // Defense in depth: a plan file may have been hand-edited, so re-validate
        // the timeout values before they are interpolated into SQL.
        MigrationTimeout.EnsureValid(plan.Settings.LockTimeout, nameof(plan.Settings.LockTimeout));
        MigrationTimeout.EnsureValid(plan.Settings.StatementTimeout, nameof(plan.Settings.StatementTimeout));

        var statements = options.Rollback ? plan.Down : plan.Up;

        if (statements.Count == 0)
        {
            logger.LogInformation("Plan contains no statements to apply.");
            return new ApplyResult { Executed = 0, Skipped = 0, DryRun = options.DryRun };
        }

        var toExecute = new List<PlanStatement>();
        var skipped = 0;

        foreach (var statement in statements)
        {
            // Safe plans never execute destructive statements automatically.
            if (plan.Settings.Safe && statement.Destructive)
            {
                logger.LogWarning("Skipping destructive statement (safe plan): {Sql}", statement.Sql);
                skipped++;
                continue;
            }

            toExecute.Add(statement);
        }

        if (options.DryRun)
        {
            foreach (var statement in toExecute)
                progress.Step($"[dry-run] {FirstLine(statement.Sql)}");

            return new ApplyResult { Executed = 0, Skipped = skipped, DryRun = true };
        }

        var transactional = toExecute.Where(s => !s.RunsOutsideTransaction).ToList();
        var concurrent = toExecute.Where(s => s.RunsOutsideTransaction).ToList();

        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var executed = 0;

        async Task RunConcurrentAsync()
        {
            foreach (var statement in concurrent)
            {
                progress.Step($"Applying (concurrent): {FirstLine(statement.Sql)}");
                await ExecuteAsync(connection, plan.Settings, statement.Sql, cancellationToken, options.CommandTimeoutSeconds);
                executed++;
            }
        }

        async Task RunTransactionalAsync()
        {
            if (transactional.Count == 0)
                return;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await ApplySessionSettingsAsync(connection, transaction, plan.Settings, cancellationToken, options.CommandTimeoutSeconds);

                foreach (var statement in transactional)
                {
                    progress.Step($"Applying: {FirstLine(statement.Sql)}");
                    await ExecuteAsync(connection, statement.Sql, transaction, cancellationToken, options.CommandTimeoutSeconds);
                    executed++;
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // Up: transaction first, then concurrent. Down: concurrent first, then transaction.
        if (options.Rollback)
        {
            await RunConcurrentAsync();
            await RunTransactionalAsync();
        }
        else
        {
            await RunTransactionalAsync();
            await RunConcurrentAsync();
        }

        return new ApplyResult { Executed = executed, Skipped = skipped, DryRun = false };
    }

    private static async Task ApplySessionSettingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationPlanSettings settings,
        CancellationToken cancellationToken,
        int commandTimeoutSeconds = 0)
    {
        if (!string.IsNullOrWhiteSpace(settings.LockTimeout))
            await ExecuteAsync(connection, $"SET LOCAL lock_timeout = {SqlLiteral.String(settings.LockTimeout)}", transaction, cancellationToken, commandTimeoutSeconds);

        if (!string.IsNullOrWhiteSpace(settings.StatementTimeout))
            await ExecuteAsync(connection, $"SET LOCAL statement_timeout = {SqlLiteral.String(settings.StatementTimeout)}", transaction, cancellationToken, commandTimeoutSeconds);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        MigrationPlanSettings settings,
        string sql,
        CancellationToken cancellationToken,
        int commandTimeoutSeconds = 0)
    {
        // Concurrent statements run with autocommit; apply session-level timeouts first.
        if (!string.IsNullOrWhiteSpace(settings.LockTimeout))
            await ExecuteAsync(connection, $"SET lock_timeout = {SqlLiteral.String(settings.LockTimeout)}", null, cancellationToken, commandTimeoutSeconds);

        await ExecuteAsync(connection, sql, null, cancellationToken, commandTimeoutSeconds);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken,
        int? commandTimeout = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeout ?? 0
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FirstLine(string sql)
    {
        var span = sql.AsSpan().Trim();

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '\r' || span[i] == '\n')
            {
                var line = span[..i].TrimEnd();
                return line.Length <= 160 ? line.ToString() + " …" : line[..160].ToString() + "…";
            }
        }

        return span.Length <= 160 ? span.ToString() : span[..160].ToString() + "…";
    }
}
