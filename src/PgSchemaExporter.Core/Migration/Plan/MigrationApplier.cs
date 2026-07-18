using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Scripting;
using System.Security.Cryptography;
using System.Text;

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
        public int Resumed { get; init; }
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

        /// <summary>Skip statements that are already recorded in the migration journal for this plan and direction.</summary>
        public bool Resume { get; init; }

        /// <summary>Name of the journal table used to track applied statements.</summary>
        public string JournalTableName { get; init; } = "pgschema_migration_journal";
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
            return new ApplyResult { Executed = 0, Skipped = 0, Resumed = 0, DryRun = options.DryRun };
        }

        var planId = GetPlanId(plan);
        var direction = options.Rollback ? "down" : "up";

        NpgsqlConnection? connection = null;
        IReadOnlySet<(int Index, string Hash)> applied = new HashSet<(int Index, string Hash)>();

        try
        {
            if (!options.DryRun)
            {
                connection = new NpgsqlConnection(options.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                // Create the journal table (autocommit) so we can record each applied statement.
                await EnsureJournalTableAsync(connection, options.JournalTableName, cancellationToken);

                // When resuming, skip statements already recorded in the journal for this plan/direction.
                if (options.Resume)
                    applied = await LoadJournalAsync(connection, options.JournalTableName, planId, direction, cancellationToken);
            }

            var toExecute = new List<PlanStatement>();
            var statementIndex = new Dictionary<PlanStatement, int>();
            var skipped = 0;
            var resumed = 0;

            for (var i = 0; i < statements.Count; i++)
            {
                var statement = statements[i];

                // Safe plans never execute destructive statements automatically.
                if (plan.Settings.Safe && statement.Destructive)
                {
                    logger.LogWarning("Skipping destructive statement (safe plan): {Sql}", statement.Sql);
                    skipped++;
                    continue;
                }

                if (options.Resume && applied.Contains((i, ComputeStatementHash(statement.Sql))))
                {
                    logger.LogInformation("Skipping already-applied statement {Index}: {Sql}", i, FirstLine(statement.Sql));
                    resumed++;
                    continue;
                }

                statementIndex[statement] = i;
                toExecute.Add(statement);
            }

            if (options.DryRun)
            {
                foreach (var statement in toExecute)
                    progress.Step($"[dry-run] {FirstLine(statement.Sql)}");

                return new ApplyResult { Executed = 0, Skipped = skipped, Resumed = resumed, DryRun = true };
            }

            var transactional = toExecute.Where(s => !s.RunsOutsideTransaction).ToList();
            var concurrent = toExecute.Where(s => s.RunsOutsideTransaction).ToList();

            var executed = 0;

            async Task RunConcurrentAsync()
            {
                foreach (var statement in concurrent)
                {
                    var index = statementIndex[statement];
                    progress.Step($"Applying (concurrent): {FirstLine(statement.Sql)}");
                    await ExecuteAsync(connection!, plan.Settings, statement.Sql, cancellationToken, options.CommandTimeoutSeconds);
                    await WriteJournalAsync(connection!, null, options.JournalTableName, planId, direction, index, statement.Sql, cancellationToken);
                    executed++;
                }
            }

            async Task RunTransactionalAsync()
            {
                if (transactional.Count == 0)
                    return;

                await using var transaction = await connection!.BeginTransactionAsync(cancellationToken);
                try
                {
                    await ApplySessionSettingsAsync(connection!, transaction, plan.Settings, cancellationToken, options.CommandTimeoutSeconds);

                    foreach (var statement in transactional)
                    {
                        var index = statementIndex[statement];
                        progress.Step($"Applying: {FirstLine(statement.Sql)}");
                        await ExecuteAsync(connection!, statement.Sql, transaction, cancellationToken, options.CommandTimeoutSeconds);
                        await WriteJournalAsync(connection!, transaction, options.JournalTableName, planId, direction, index, statement.Sql, cancellationToken);
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

            return new ApplyResult { Executed = executed, Skipped = skipped, Resumed = resumed, DryRun = false };
        }
        finally
        {
            if (connection is not null)
                await connection.DisposeAsync();
        }
    }

    private static string GetPlanId(MigrationPlan plan) =>
        string.IsNullOrWhiteSpace(plan.Name) ? "unnamed" : plan.Name!;

    private static string ComputeStatementHash(string sql)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<HashSet<(int Index, string Hash)>> LoadJournalAsync(
        NpgsqlConnection connection,
        string tableName,
        string planName,
        string direction,
        CancellationToken cancellationToken)
    {
        var sql = $"SELECT sequence_index, statement_hash FROM {tableName} WHERE plan_name = $1 AND direction = $2";
        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters =
            {
                new NpgsqlParameter { Value = planName },
                new NpgsqlParameter { Value = direction }
            }
        };

        var result = new HashSet<(int Index, string Hash)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return result;
    }

    private static async Task WriteJournalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string tableName,
        string planName,
        string direction,
        int sequenceIndex,
        string statementSql,
        CancellationToken cancellationToken)
    {
        var hash = ComputeStatementHash(statementSql);
        var sql = $@"
            INSERT INTO {tableName} (plan_name, direction, sequence_index, statement_hash, statement_sql)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (plan_name, direction, sequence_index) DO NOTHING;";

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            Parameters =
            {
                new NpgsqlParameter { Value = planName },
                new NpgsqlParameter { Value = direction },
                new NpgsqlParameter { Value = sequenceIndex },
                new NpgsqlParameter { Value = hash },
                new NpgsqlParameter { Value = statementSql }
            }
        };

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureJournalTableAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = $@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                plan_name TEXT NOT NULL,
                direction TEXT NOT NULL,
                sequence_index INT NOT NULL,
                statement_hash TEXT NOT NULL,
                statement_sql TEXT NOT NULL,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (plan_name, direction, sequence_index)
            );";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 0
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
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
