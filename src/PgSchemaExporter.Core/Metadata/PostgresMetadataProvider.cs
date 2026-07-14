using Npgsql;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PgSchemaExporter.Core.Diagnostics;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;

namespace PgSchemaExporter.Core.Metadata;

public sealed class PostgresMetadataProvider : IMetadataProvider
{
    private const int MaxParallelQueries = 8;

    // Number of object kinds loaded; used to report progress totals.
    private const int ObjectKindCount = 22;

    public async Task<DatabaseModel> LoadAsync(
        string connectionString,
        ExportOptions options,
        IProgressReporter? progress = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= NullProgressReporter.Instance;
        logger ??= NullLogger.Instance;

        var stopwatch = Stopwatch.StartNew();
        progress.Start(
            options.Parallel ? "Loading metadata (parallel)" : "Loading metadata",
            ObjectKindCount);

        var model = options.Parallel
            ? await LoadParallelAsync(connectionString, options, progress, logger, cancellationToken)
            : await LoadSequentialAsync(connectionString, options, progress, logger, cancellationToken);

        stopwatch.Stop();
        logger.LogInformation("Metadata loaded in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        progress.Complete($"Metadata loaded in {stopwatch.ElapsedMilliseconds} ms");
        return model;
    }

    private static async Task<DatabaseModel> LoadSequentialAsync(
        string connectionString,
        ExportOptions options,
        IProgressReporter progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        async Task<IReadOnlyList<T>> Load<T>(
            string kind,
            bool include,
            Func<Task<IReadOnlyList<T>>> query)
        {
            if (!include)
                return [];

            progress.Step($"Loading {kind}");
            var result = await query();
            logger.LogDebug("Loaded {Count} {Kind}", result.Count, kind);
            return result;
        }

        return new DatabaseModel
        {
            Schemas = await Load("schemas", options.Include.Schemas, () => GetSchemasAsync(connection, options, cancellationToken)),
            Extensions = await Load("extensions", options.Include.Extensions, () => GetExtensionsAsync(connection, cancellationToken)),
            Types = await Load("types", options.Include.Types, () => GetTypesAsync(connection, options, cancellationToken)),
            Sequences = await Load("sequences", options.Include.Sequences, () => GetSequencesAsync(connection, options, cancellationToken)),
            Domains = await Load("domains", options.Include.Domains, () => GetDomainsAsync(connection, options, cancellationToken)),
            ForeignTables = await Load("foreign tables", options.Include.ForeignTables, () => GetForeignTablesAsync(connection, options, cancellationToken)),
            Tables = await Load("tables", options.Include.Tables, () => GetTablesAsync(connection, options, cancellationToken)),
            Constraints = await Load("constraints", options.Include.Constraints, () => GetConstraintsAsync(connection, options, cancellationToken)),
            Indexes = await Load("indexes", options.Include.Indexes, () => GetIndexesAsync(connection, options, cancellationToken)),
            Views = await Load("views", options.Include.Views, () => GetViewsAsync(connection, options, cancellationToken)),
            Triggers = await Load("triggers", options.Include.Triggers, () => GetTriggersAsync(connection, options, cancellationToken)),
            EventTriggers = await Load("event triggers", options.Include.EventTriggers, () => GetEventTriggersAsync(connection, cancellationToken)),
            Rules = await Load("rules", options.Include.Rules, () => GetRulesAsync(connection, options, cancellationToken)),
            Aggregates = await Load("aggregates", options.Include.Aggregates, () => GetAggregatesAsync(connection, options, cancellationToken)),
            Operators = await Load("operators", options.Include.Operators, () => GetOperatorsAsync(connection, options, cancellationToken)),
            Casts = await Load("casts", options.Include.Casts, () => GetCastsAsync(connection, options, cancellationToken)),
            Publications = await Load("publications", options.Include.Publications, () => GetPublicationsAsync(connection, cancellationToken)),
            Subscriptions = await Load("subscriptions", options.Include.Subscriptions, () => GetSubscriptionsAsync(connection, cancellationToken)),
            Policies = await Load("policies", options.Include.Policies, () => GetPoliciesAsync(connection, options, cancellationToken)),
            Comments = await Load("comments", options.Include.Comments, () => GetCommentsAsync(connection, options, cancellationToken)),
            Grants = await Load("grants", options.Include.Grants, () => GetGrantsAsync(connection, options, cancellationToken)),
            Functions = await Load("functions", options.Include.Functions, () => GetFunctionsAsync(connection, options, cancellationToken))
        };
    }

    private static async Task<DatabaseModel> LoadParallelAsync(
        string connectionString,
        ExportOptions options,
        IProgressReporter progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // NpgsqlConnection allows only one active command at a time, so each parallel
        // query runs on its own pooled connection. Concurrency is bounded to avoid
        // exhausting server connection limits.
        using var gate = new SemaphoreSlim(MaxParallelQueries);

        Task<IReadOnlyList<T>> Run<T>(string kind, bool include, Func<NpgsqlConnection, CancellationToken, Task<IReadOnlyList<T>>> query)
        {
            if (!include)
                return Task.FromResult<IReadOnlyList<T>>([]);

            return RunOnOwnConnectionAsync(connectionString, gate, async (c, ct) =>
            {
                var result = await query(c, ct);
                progress.Step($"Loaded {kind}");
                logger.LogDebug("Loaded {Count} {Kind}", result.Count, kind);
                return result;
            }, cancellationToken);
        }

        var schemas = Run("schemas", options.Include.Schemas, (c, ct) => GetSchemasAsync(c, options, ct));
        var extensions = Run("extensions", options.Include.Extensions, GetExtensionsAsync);
        var types = Run("types", options.Include.Types, (c, ct) => GetTypesAsync(c, options, ct));
        var sequences = Run("sequences", options.Include.Sequences, (c, ct) => GetSequencesAsync(c, options, ct));
        var domains = Run("domains", options.Include.Domains, (c, ct) => GetDomainsAsync(c, options, ct));
        var foreignTables = Run("foreign tables", options.Include.ForeignTables, (c, ct) => GetForeignTablesAsync(c, options, ct));
        var tables = Run("tables", options.Include.Tables, (c, ct) => GetTablesAsync(c, options, ct));
        var constraints = Run("constraints", options.Include.Constraints, (c, ct) => GetConstraintsAsync(c, options, ct));
        var indexes = Run("indexes", options.Include.Indexes, (c, ct) => GetIndexesAsync(c, options, ct));
        var views = Run("views", options.Include.Views, (c, ct) => GetViewsAsync(c, options, ct));
        var triggers = Run("triggers", options.Include.Triggers, (c, ct) => GetTriggersAsync(c, options, ct));
        var eventTriggers = Run("event triggers", options.Include.EventTriggers, GetEventTriggersAsync);
        var rules = Run("rules", options.Include.Rules, (c, ct) => GetRulesAsync(c, options, ct));
        var aggregates = Run("aggregates", options.Include.Aggregates, (c, ct) => GetAggregatesAsync(c, options, ct));
        var operators = Run("operators", options.Include.Operators, (c, ct) => GetOperatorsAsync(c, options, ct));
        var casts = Run("casts", options.Include.Casts, (c, ct) => GetCastsAsync(c, options, ct));
        var publications = Run("publications", options.Include.Publications, GetPublicationsAsync);
        var subscriptions = Run("subscriptions", options.Include.Subscriptions, GetSubscriptionsAsync);
        var policies = Run("policies", options.Include.Policies, (c, ct) => GetPoliciesAsync(c, options, ct));
        var comments = Run("comments", options.Include.Comments, (c, ct) => GetCommentsAsync(c, options, ct));
        var grants = Run("grants", options.Include.Grants, (c, ct) => GetGrantsAsync(c, options, ct));
        var functions = Run("functions", options.Include.Functions, (c, ct) => GetFunctionsAsync(c, options, ct));

        await Task.WhenAll(
            schemas, extensions, types, sequences, domains, foreignTables, tables,
            constraints, indexes, views, triggers, eventTriggers, rules, aggregates,
            operators, casts, publications, subscriptions, policies, comments, grants, functions);

        return new DatabaseModel
        {
            Schemas = await schemas,
            Extensions = await extensions,
            Types = await types,
            Sequences = await sequences,
            Domains = await domains,
            ForeignTables = await foreignTables,
            Tables = await tables,
            Constraints = await constraints,
            Indexes = await indexes,
            Views = await views,
            Triggers = await triggers,
            EventTriggers = await eventTriggers,
            Rules = await rules,
            Aggregates = await aggregates,
            Operators = await operators,
            Casts = await casts,
            Publications = await publications,
            Subscriptions = await subscriptions,
            Policies = await policies,
            Comments = await comments,
            Grants = await grants,
            Functions = await functions
        };
    }

    private static async Task<IReadOnlyList<T>> RunOnOwnConnectionAsync<T>(
        string connectionString,
        SemaphoreSlim gate,
        Func<NpgsqlConnection, CancellationToken, Task<IReadOnlyList<T>>> query,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return await query(connection, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<IReadOnlyList<DbSchema>> GetSchemasAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT nspname AS schema_name,
                   pg_get_userbyid(nspowner) AS owner_name
            FROM pg_namespace
            WHERE nspname = ANY(@schemas)
              AND NOT nspname = ANY(@excludeSchemas)
            ORDER BY nspname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbSchema>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbSchema
            {
                Name = reader.GetString(0),
                Owner = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbExtension>> GetExtensionsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT e.extname,
                   n.nspname,
                   e.extversion
            FROM pg_extension e
            JOIN pg_namespace n ON n.oid = e.extnamespace
            ORDER BY e.extname;";

        await using var command = new NpgsqlCommand(sql, connection);
        var result = new List<DbExtension>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbExtension
            {
                Name = reader.GetString(0),
                Schema = reader.GetString(1),
                Version = reader.GetString(2)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbType>> GetTypesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   t.typname AS type_name,
                   t.typtype::text AS type_kind,
                   array_remove(array_agg(e.enumlabel ORDER BY e.enumsortorder), NULL) AS enum_labels,
                   CASE WHEN t.typtype = 'c' THEN
                       'CREATE TYPE ' || quote_ident(n.nspname) || '.' || quote_ident(t.typname) || ' AS (' ||
                       COALESCE((
                           SELECT string_agg(quote_ident(a.attname) || ' ' || format_type(a.atttypid, a.atttypmod), ', ' ORDER BY a.attnum)
                           FROM pg_attribute a
                           WHERE a.attrelid = t.typrelid AND a.attnum > 0 AND NOT a.attisdropped
                       ), '') || ');'
                   END AS composite_def,
                   CASE WHEN t.typtype = 'r' THEN
                       'CREATE TYPE ' || quote_ident(n.nspname) || '.' || quote_ident(t.typname) || ' AS RANGE (subtype = ' ||
                       format_type((SELECT rngsubtype FROM pg_range WHERE rngtypid = t.oid), NULL) || ');'
                   END AS range_def
            FROM pg_type t
            JOIN pg_namespace n ON n.oid = t.typnamespace
            LEFT JOIN pg_enum e ON e.enumtypid = t.oid
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
              AND t.typtype IN ('e', 'c', 'r')
              AND (t.typtype <> 'c'
                   OR EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind = 'c'))
            GROUP BY n.nspname, t.typname, t.typtype, t.oid, t.typrelid
            ORDER BY n.nspname, t.typname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbType>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var labels = reader.IsDBNull(3)
                ? []
                : reader.GetFieldValue<string[]>(3).ToList();

            result.Add(new DbType
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                Kind = reader.GetString(2),
                EnumLabels = labels,
                CompositeDefinition = reader.IsDBNull(4) ? null : reader.GetString(4),
                RangeDefinition = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbSequence>> GetSequencesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT sequence_schema,
                   sequence_name,
                   data_type,
                   start_value::bigint,
                   minimum_value::bigint,
                   maximum_value::bigint,
                   increment::bigint,
                   cycle_option = 'YES' AS cycle
            FROM information_schema.sequences
            WHERE sequence_schema = ANY(@schemas)
              AND NOT sequence_schema = ANY(@excludeSchemas)
            ORDER BY sequence_schema, sequence_name;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbSequence>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbSequence
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                DataType = reader.GetString(2),
                StartValue = reader.GetInt64(3),
                MinimumValue = reader.GetInt64(4),
                MaximumValue = reader.GetInt64(5),
                Increment = reader.GetInt64(6),
                Cycle = reader.GetBoolean(7)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbDomain>> GetDomainsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   t.typname AS domain_name,
                   pg_get_userbyid(n.nspowner) AS owner_name,
                   format_type(t.typbasetype, t.typtypmod) AS base_type,
                   t.typdefault AS default_value
            FROM pg_type t
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE t.typtype = 'd'
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
            ORDER BY n.nspname, t.typname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbDomain>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var definition = $"{reader.GetString(3)}";
            if (!reader.IsDBNull(4) && !string.IsNullOrWhiteSpace(reader.GetString(4)))
                definition += $" DEFAULT {reader.GetString(4)}";

            result.Add(new DbDomain
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                Definition = definition
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbForeignTable>> GetForeignTablesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   c.relname AS foreign_table_name,
                   'CREATE FOREIGN TABLE ' || quote_ident(n.nspname) || '.' || quote_ident(c.relname) || ' (' ||
                   string_agg(
                       quote_ident(a.attname) || ' ' || pg_catalog.format_type(a.atttypid, a.atttypmod),
                       ', ' ORDER BY a.attnum) ||
                   ') SERVER ' || quote_ident(s.srvname) ||
                   COALESCE(' OPTIONS (' || (
                       SELECT string_agg(
                           quote_ident(split_part(o, '=', 1)) || ' ' || quote_literal(split_part(o, '=', 2)),
                           ', ')
                       FROM unnest(f.ftoptions) AS o
                   ) || ')', '') AS definition
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_foreign_table f ON f.ftrelid = c.oid
            JOIN pg_foreign_server s ON s.oid = f.ftserver
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE c.relkind = 'f'
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
            GROUP BY n.nspname, c.relname, s.srvname, f.ftoptions
            ORDER BY n.nspname, c.relname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbForeignTable>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbForeignTable
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                Definition = reader.IsDBNull(2) ? "" : reader.GetString(2)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbTable>> GetTablesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                c.table_schema,
                c.table_name,
                c.column_name,
                c.udt_schema,
                c.udt_name,
                c.data_type,
                c.is_nullable,
                c.column_default,
                c.ordinal_position,
                c.character_maximum_length,
                c.numeric_precision,
                c.numeric_scale
            FROM information_schema.columns c
            JOIN information_schema.tables t
              ON t.table_schema = c.table_schema
             AND t.table_name = c.table_name
            WHERE c.table_schema = ANY(@schemas)
              AND NOT c.table_schema = ANY(@excludeSchemas)
              AND t.table_type = 'BASE TABLE'
            ORDER BY c.table_schema, c.table_name, c.ordinal_position;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var tables = new Dictionary<string, DbTable>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);
            var key = $"{schema}.{tableName}";

            if (!tables.TryGetValue(key, out var table))
            {
                table = new DbTable { Schema = schema, Name = tableName };
                tables.Add(key, table);
            }

            var dataType = reader.GetString(5);
            var udtSchema = reader.GetString(3);
            var udtName = reader.GetString(4);

            table.Columns.Add(new DbColumn
            {
                Name = reader.GetString(2),
                DataType = NormalizeDataType(dataType, udtSchema, udtName),
                IsNullable = reader.GetString(6) == "YES",
                DefaultValue = reader.IsDBNull(7) ? null : reader.GetString(7),
                OrdinalPosition = reader.GetInt32(8),
                CharacterMaximumLength = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                NumericPrecision = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                NumericScale = reader.IsDBNull(11) ? null : reader.GetInt32(11)
            });
        }

        return tables.Values.ToList();
    }

    private static async Task<IReadOnlyList<DbConstraint>> GetConstraintsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                ns.nspname AS schema_name,
                tbl.relname AS table_name,
                con.conname AS constraint_name,
                con.contype::text AS constraint_type,
                pg_get_constraintdef(con.oid, true) AS definition
            FROM pg_constraint con
            JOIN pg_class tbl ON tbl.oid = con.conrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            WHERE ns.nspname = ANY(@schemas)
              AND NOT ns.nspname = ANY(@excludeSchemas)
              AND con.contype IN ('p', 'u', 'c', 'f')
            ORDER BY ns.nspname, tbl.relname,
                     CASE con.contype
                        WHEN 'p' THEN 1
                        WHEN 'u' THEN 2
                        WHEN 'c' THEN 3
                        WHEN 'f' THEN 4
                        ELSE 99
                     END,
                     con.conname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbConstraint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbConstraint
            {
                Schema = reader.GetString(0),
                TableName = reader.GetString(1),
                Name = reader.GetString(2),
                Type = reader.GetString(3),
                Definition = reader.GetString(4)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbIndex>> GetIndexesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                ns.nspname AS schema_name,
                tbl.relname AS table_name,
                idx.relname AS index_name,
                i.indisprimary,
                i.indisunique,
                pg_get_indexdef(i.indexrelid) AS definition
            FROM pg_index i
            JOIN pg_class tbl ON tbl.oid = i.indrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            JOIN pg_class idx ON idx.oid = i.indexrelid
            WHERE ns.nspname = ANY(@schemas)
              AND NOT ns.nspname = ANY(@excludeSchemas)
              AND NOT i.indisprimary
            ORDER BY ns.nspname, tbl.relname, idx.relname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbIndex>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbIndex
            {
                Schema = reader.GetString(0),
                TableName = reader.GetString(1),
                Name = reader.GetString(2),
                IsPrimary = reader.GetBoolean(3),
                IsUnique = reader.GetBoolean(4),
                Definition = reader.GetString(5)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbView>> GetViewsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   c.relname AS view_name,
                   pg_get_viewdef(c.oid, true) AS definition,
                   c.relkind = 'm' AS is_materialized
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('v', 'm')
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
            ORDER BY n.nspname, c.relname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbView
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                Definition = reader.GetString(2),
                IsMaterialized = reader.GetBoolean(3)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbTrigger>> GetTriggersAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   tbl.relname AS table_name,
                   trg.tgname AS trigger_name,
                   pg_get_triggerdef(trg.oid, true) AS definition
            FROM pg_trigger trg
            JOIN pg_class tbl ON tbl.oid = trg.tgrelid
            JOIN pg_namespace n ON n.oid = tbl.relnamespace
            WHERE NOT trg.tgisinternal
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
            ORDER BY n.nspname, tbl.relname, trg.tgname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbTrigger>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbTrigger
            {
                Schema = reader.GetString(0),
                TableSchema = reader.GetString(0),
                TableName = reader.GetString(1),
                Name = reader.GetString(2),
                Definition = reader.GetString(3)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbComment>> GetCommentsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   'SCHEMA' AS object_type,
                   n.nspname AS object_name,
                   NULL::text AS sub_object,
                   format('COMMENT ON SCHEMA %I IS %L', n.nspname, obj_description(n.oid, 'pg_namespace')) AS definition
            FROM pg_namespace n
            WHERE obj_description(n.oid, 'pg_namespace') IS NOT NULL
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)

            UNION ALL

            SELECT n.nspname,
                   CASE c.relkind
                       WHEN 'r' THEN 'TABLE'
                       WHEN 'v' THEN 'VIEW'
                       WHEN 'm' THEN 'MATERIALIZED VIEW'
                       WHEN 'S' THEN 'SEQUENCE'
                       WHEN 'f' THEN 'FOREIGN TABLE'
                       ELSE 'TABLE'
                   END AS object_type,
                   c.relname AS object_name,
                   NULL::text AS sub_object,
                   format(
                       'COMMENT ON %s %I.%I IS %L',
                       CASE c.relkind
                           WHEN 'r' THEN 'TABLE'
                           WHEN 'v' THEN 'VIEW'
                           WHEN 'm' THEN 'MATERIALIZED VIEW'
                           WHEN 'S' THEN 'SEQUENCE'
                           WHEN 'f' THEN 'FOREIGN TABLE'
                           ELSE 'TABLE'
                       END,
                       n.nspname,
                       c.relname,
                       obj_description(c.oid, 'pg_class')
                   ) AS definition
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE obj_description(c.oid, 'pg_class') IS NOT NULL
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)

            UNION ALL

            SELECT n.nspname,
                   'COLUMN' AS object_type,
                   c.relname AS object_name,
                   a.attname AS sub_object,
                   format('COMMENT ON COLUMN %I.%I.%I IS %L', n.nspname, c.relname, a.attname, col_description(a.attrelid, a.attnum)) AS definition
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE a.attnum > 0
              AND NOT a.attisdropped
              AND col_description(a.attrelid, a.attnum) IS NOT NULL
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)

            UNION ALL

            SELECT n.nspname,
                   'TYPE' AS object_type,
                   t.typname AS object_name,
                   NULL::text AS sub_object,
                   format('COMMENT ON TYPE %I.%I IS %L', n.nspname, t.typname, obj_description(t.oid, 'pg_type')) AS definition
            FROM pg_type t
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE obj_description(t.oid, 'pg_type') IS NOT NULL
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
              AND t.typtype IN ('c', 'd', 'e', 'r', 'p')

            UNION ALL

            SELECT n.nspname,
                   'FUNCTION' AS object_type,
                   p.proname AS object_name,
                   pg_get_function_identity_arguments(p.oid) AS sub_object,
                   format('COMMENT ON FUNCTION %I.%I(%s) IS %L', n.nspname, p.proname, pg_get_function_identity_arguments(p.oid), obj_description(p.oid, 'pg_proc')) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE obj_description(p.oid, 'pg_proc') IS NOT NULL
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
              AND p.prokind IN ('f', 'p')

            UNION ALL

            SELECT n.nspname,
                   'CONSTRAINT' AS object_type,
                   tbl.relname AS object_name,
                   con.conname AS sub_object,
                   format('COMMENT ON CONSTRAINT %I ON %I.%I IS %L', con.conname, n.nspname, tbl.relname, obj_description(con.oid, 'pg_constraint')) AS definition
            FROM pg_constraint con
            JOIN pg_class tbl ON tbl.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = tbl.relnamespace
            WHERE obj_description(con.oid, 'pg_constraint') IS NOT NULL
                            AND n.nspname = ANY(@schemas)
                            AND NOT n.nspname = ANY(@excludeSchemas);";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbComment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbComment
            {
                Schema = reader.GetString(0),
                ObjectType = reader.GetString(1),
                ObjectName = reader.GetString(2),
                SubObject = reader.IsDBNull(3) ? null : reader.GetString(3),
                Definition = reader.GetString(4)
            });
        }

        var ordered = result
            .OrderBy(x => x.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SubObject ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ordered;
    }

    private static async Task<IReadOnlyList<DbGrant>> GetGrantsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   CASE c.relkind WHEN 'S' THEN 'SEQUENCE' ELSE 'TABLE' END AS object_type,
                   c.relname AS object_name,
                   NULL::text AS sub_object,
                   format(
                       'GRANT %s ON %s %I.%I TO %s%s',
                       acl.privilege_type,
                       CASE c.relkind WHEN 'S' THEN 'SEQUENCE' ELSE 'TABLE' END,
                       n.nspname,
                       c.relname,
                       CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(acl.grantee)) END,
                       CASE WHEN acl.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END
                   ) AS definition
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            CROSS JOIN LATERAL aclexplode(c.relacl) AS acl
            WHERE c.relkind IN ('r', 'p', 'f', 'v', 'm', 'S')
              AND n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)

            UNION ALL

            SELECT n.nspname AS schema_name,
                   'SCHEMA' AS object_type,
                   n.nspname AS object_name,
                   NULL::text AS sub_object,
                   format(
                       'GRANT %s ON SCHEMA %I TO %s%s',
                       acl.privilege_type,
                       n.nspname,
                       CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(acl.grantee)) END,
                       CASE WHEN acl.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END
                   ) AS definition
            FROM pg_namespace n
            CROSS JOIN LATERAL aclexplode(n.nspacl) AS acl
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)

            UNION ALL

            SELECT n.nspname AS schema_name,
                   'TYPE' AS object_type,
                   t.typname AS object_name,
                   NULL::text AS sub_object,
                   format(
                       'GRANT %s ON TYPE %I.%I TO %s%s',
                       acl.privilege_type,
                       n.nspname,
                       t.typname,
                       CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(acl.grantee)) END,
                       CASE WHEN acl.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END
                   ) AS definition
            FROM pg_type t
            JOIN pg_namespace n ON n.oid = t.typnamespace
            CROSS JOIN LATERAL aclexplode(t.typacl) AS acl
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
              AND t.typtype IN ('c', 'd', 'e', 'r', 'p')

            UNION ALL

            SELECT n.nspname AS schema_name,
                   'FUNCTION' AS object_type,
                   p.proname AS object_name,
                   pg_get_function_identity_arguments(p.oid) AS sub_object,
                   format(
                       'GRANT %s ON FUNCTION %I.%I(%s) TO %s%s',
                       acl.privilege_type,
                       n.nspname,
                       p.proname,
                       pg_get_function_identity_arguments(p.oid),
                       CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(acl.grantee)) END,
                       CASE WHEN acl.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END
                   ) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            CROSS JOIN LATERAL aclexplode(p.proacl) AS acl
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
                            AND p.prokind IN ('f', 'p');";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbGrant>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbGrant
            {
                Schema = reader.GetString(0),
                ObjectType = reader.GetString(1),
                ObjectName = reader.GetString(2),
                SubObject = reader.IsDBNull(3) ? null : reader.GetString(3),
                Definition = reader.GetString(4)
            });
        }

        var ordered = result
            .OrderBy(x => x.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ObjectType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SubObject ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ordered;
    }

    private static async Task<IReadOnlyList<DbPolicy>> GetPoliciesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        var usePolicyDef = await PolicyDefFunctionExistsAsync(connection, cancellationToken);

        var sql = usePolicyDef
            ? @"
                SELECT n.nspname AS schema_name,
                       tbl.relname AS table_name,
                       pol.polname AS policy_name,
                       pg_get_policydef(pol.oid) AS definition
                FROM pg_policy pol
                JOIN pg_class tbl ON tbl.oid = pol.polrelid
                JOIN pg_namespace n ON n.oid = tbl.relnamespace
                WHERE n.nspname = ANY(@schemas)
                  AND NOT n.nspname = ANY(@excludeSchemas)
                ORDER BY n.nspname, tbl.relname, pol.polname;"
            : @"
                SELECT n.nspname AS schema_name,
                       tbl.relname AS table_name,
                       pol.polname AS policy_name,
                       format(
                           'CREATE POLICY %I ON %I.%I %s FOR %s TO %s%s%s',
                           pol.polname,
                           n.nspname,
                           tbl.relname,
                           CASE WHEN pol.polpermissive THEN 'AS PERMISSIVE' ELSE 'AS RESTRICTIVE' END,
                           CASE pol.polcmd
                               WHEN 'r' THEN 'SELECT'
                               WHEN 'a' THEN 'INSERT'
                               WHEN 'w' THEN 'UPDATE'
                               WHEN 'd' THEN 'DELETE'
                               ELSE ''
                           END,
                           CASE WHEN coalesce(array_remove(pol.polroles, 0::oid), '{}'::oid[]) = '{}' THEN 'PUBLIC' ELSE array_to_string(ARRAY(SELECT quote_ident(pg_get_userbyid(r)) FROM unnest(array_remove(pol.polroles, 0::oid)) AS r WHERE r <> 0), ', ') END,
                           CASE WHEN pol.polqual IS NULL THEN '' ELSE ' USING (' || pg_get_expr(pol.polqual, pol.polrelid) || ')' END,
                           CASE WHEN pol.polwithcheck IS NULL THEN '' ELSE ' WITH CHECK (' || pg_get_expr(pol.polwithcheck, pol.polrelid) || ')' END
                       ) AS definition
                FROM pg_policy pol
                JOIN pg_class tbl ON tbl.oid = pol.polrelid
                JOIN pg_namespace n ON n.oid = tbl.relnamespace
                WHERE n.nspname = ANY(@schemas)
                  AND NOT n.nspname = ANY(@excludeSchemas)
                ORDER BY n.nspname, tbl.relname, pol.polname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbPolicy>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbPolicy
            {
                Schema = reader.GetString(0),
                TableSchema = reader.GetString(0),
                TableName = reader.GetString(1),
                Name = reader.GetString(2),
                Definition = reader.GetString(3)
            });
        }

        return result;
    }

    private static async Task<bool> PolicyDefFunctionExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT EXISTS (
                SELECT 1
                FROM pg_proc p
                JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE p.proname = 'pg_get_policydef'
                  AND n.nspname = 'pg_catalog'
            );";

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    private static async Task<IReadOnlyList<DbFunction>> GetFunctionsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   p.proname AS function_name,
                   pg_get_function_identity_arguments(p.oid) AS identity_args,
                   pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
              -- pg_proc also contains aggregates and window functions.
              -- pg_get_functiondef() raises 42809 for aggregates such as PostGIS st_clusterintersecting.
              -- Export only normal functions and procedures.
              AND p.prokind IN ('f', 'p')
              -- Extension-owned routines should be recreated by CREATE EXTENSION,
              -- not exported individually. This avoids exporting PostGIS/pgcrypto internals.
              AND NOT EXISTS (
                  SELECT 1
                  FROM pg_depend d
                  JOIN pg_extension e ON e.oid = d.refobjid
                  WHERE d.objid = p.oid
                    AND d.classid = 'pg_proc'::regclass
                    AND d.deptype = 'e'
              )
            ORDER BY n.nspname, p.proname, identity_args;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbFunction>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbFunction
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                ArgumentsIdentity = reader.GetString(2),
                Definition = reader.GetString(3)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbEventTrigger>> GetEventTriggersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT et.evtname AS event_trigger_name,
                   et.evtevent AS event,
                   NULLIF(array_to_string(et.evttags, ', '), '') AS when_clause,
                   quote_ident(n.nspname) || '.' || quote_ident(p.proname) AS procedure,
                   'CREATE EVENT TRIGGER ' || quote_ident(et.evtname) ||
                   ' ON ' || et.evtevent ||
                   COALESCE(' WHEN TAG IN (' || (
                       SELECT string_agg(quote_literal(tag), ', ')
                       FROM unnest(et.evttags) AS tag
                   ) || ')', '') ||
                   ' EXECUTE FUNCTION ' || quote_ident(n.nspname) || '.' || quote_ident(p.proname) || '();' AS definition
            FROM pg_event_trigger et
            JOIN pg_proc p ON p.oid = et.evtfoid
            JOIN pg_namespace n ON n.oid = p.pronamespace
            ORDER BY et.evtname;";

        await using var command = new NpgsqlCommand(sql, connection);
        var result = new List<DbEventTrigger>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbEventTrigger
            {
                Name = reader.GetString(0),
                Event = reader.GetString(1),
                When = reader.IsDBNull(2) ? null : reader.GetString(2),
                Procedure = reader.GetString(3),
                Definition = reader.GetString(4)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbRule>> GetRulesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   c.relname AS table_name,
                   r.rulename AS rule_name,
                   pg_get_ruledef(r.oid, true) AS definition
            FROM pg_rewrite r
            JOIN pg_class c ON c.oid = r.ev_class
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
              AND r.rulename <> '_RETURN'
            ORDER BY n.nspname, c.relname, r.rulename;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbRule
            {
                Schema = reader.GetString(0),
                TableName = reader.GetString(1),
                Name = reader.GetString(2),
                Definition = reader.GetString(3)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbAggregate>> GetAggregatesAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   p.proname AS aggregate_name,
                   pg_get_function_arguments(p.oid) AS input_type,
                   format_type(a.aggtranstype, NULL) AS state_type,
                   CASE WHEN a.aggfinalfn <> 0 THEN a.aggfinalfn::regproc::text END AS finalize_func,
                   'CREATE AGGREGATE ' || quote_ident(n.nspname) || '.' || quote_ident(p.proname) ||
                   '(' || pg_get_function_arguments(p.oid) || ') (' ||
                   'SFUNC = ' || a.aggtransfn::regproc::text ||
                   ', STYPE = ' || format_type(a.aggtranstype, NULL) ||
                   COALESCE(', FINALFUNC = ' || NULLIF(a.aggfinalfn, 0)::regproc::text, '') ||
                   COALESCE(', INITCOND = ' || quote_literal(a.agginitval), '') ||
                   ');' AS definition
            FROM pg_aggregate a
            JOIN pg_proc p ON p.oid = a.aggfnoid
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
            ORDER BY n.nspname, p.proname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbAggregate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbAggregate
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                InputType = reader.GetString(2),
                StateType = reader.IsDBNull(3) ? null : reader.GetString(3),
                FinalizeFunc = reader.IsDBNull(4) ? null : reader.GetString(4),
                Definition = reader.GetString(5)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbOperator>> GetOperatorsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT n.nspname AS schema_name,
                   o.oprname AS operator_name,
                   CASE WHEN o.oprleft <> 0 THEN format_type(o.oprleft, NULL) END AS left_type,
                   CASE WHEN o.oprright <> 0 THEN format_type(o.oprright, NULL) END AS right_type,
                   format_type(o.oprresult, NULL) AS result_type,
                   'CREATE OPERATOR ' || quote_ident(n.nspname) || '.' || o.oprname || ' (' ||
                   'FUNCTION = ' || o.oprcode::regproc::text ||
                   COALESCE(', LEFTARG = ' || format_type(NULLIF(o.oprleft, 0), NULL), '') ||
                   COALESCE(', RIGHTARG = ' || format_type(NULLIF(o.oprright, 0), NULL), '') ||
                   ');' AS definition
            FROM pg_operator o
            JOIN pg_namespace n ON n.oid = o.oprnamespace
            WHERE n.nspname = ANY(@schemas)
              AND NOT n.nspname = ANY(@excludeSchemas)
            ORDER BY n.nspname, o.oprname;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbOperator>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbOperator
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1),
                LeftType = reader.IsDBNull(2) ? "" : reader.GetString(2),
                RightType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ResultType = reader.GetString(4),
                Definition = reader.GetString(5)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbCast>> GetCastsAsync(NpgsqlConnection connection, ExportOptions options, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT format_type(c.castsource, NULL) AS source_type,
                   format_type(c.casttarget, NULL) AS target_type,
                   'CREATE CAST (' || format_type(c.castsource, NULL) || ' AS ' || format_type(c.casttarget, NULL) || ')' ||
                   CASE c.castmethod
                       WHEN 'f' THEN ' WITH FUNCTION ' || c.castfunc::regprocedure::text
                       WHEN 'i' THEN ' WITH INOUT'
                       ELSE ' WITHOUT FUNCTION'
                   END ||
                   CASE c.castcontext
                       WHEN 'a' THEN ' AS ASSIGNMENT'
                       WHEN 'i' THEN ' AS IMPLICIT'
                       ELSE ''
                   END || ';' AS definition
            FROM pg_cast c
            JOIN pg_type src ON src.oid = c.castsource
            JOIN pg_type tgt ON tgt.oid = c.casttarget
            JOIN pg_namespace sn ON sn.oid = src.typnamespace
            JOIN pg_namespace tn ON tn.oid = tgt.typnamespace
            WHERE (sn.nspname = ANY(@schemas) OR tn.nspname = ANY(@schemas))
              AND NOT (sn.nspname = ANY(@excludeSchemas) AND tn.nspname = ANY(@excludeSchemas))
            ORDER BY 1, 2;";

        await using var command = new NpgsqlCommand(sql, connection);
        AddSchemaParameters(command, options);

        var result = new List<DbCast>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbCast
            {
                SourceType = reader.GetString(0),
                TargetType = reader.GetString(1),
                Definition = reader.GetString(2)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbPublication>> GetPublicationsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT p.pubname AS publication_name,
                   CASE WHEN p.puballtables THEN NULL
                        ELSE (SELECT string_agg(quote_ident(pt.schemaname) || '.' || quote_ident(pt.tablename), ', ')
                              FROM pg_publication_tables pt WHERE pt.pubname = p.pubname)
                   END AS tables,
                   'CREATE PUBLICATION ' || quote_ident(p.pubname) ||
                   CASE WHEN p.puballtables THEN ' FOR ALL TABLES'
                        ELSE COALESCE(' FOR TABLE ' || (
                                 SELECT string_agg(quote_ident(pt.schemaname) || '.' || quote_ident(pt.tablename), ', ')
                                 FROM pg_publication_tables pt WHERE pt.pubname = p.pubname), '')
                   END ||
                   ' WITH (publish = ' || quote_literal(array_to_string(ARRAY[
                       CASE WHEN p.pubinsert THEN 'insert' END,
                       CASE WHEN p.pubupdate THEN 'update' END,
                       CASE WHEN p.pubdelete THEN 'delete' END,
                       CASE WHEN p.pubtruncate THEN 'truncate' END
                   ], ', ')) || ');' AS definition
            FROM pg_publication p
            ORDER BY p.pubname;";

        await using var command = new NpgsqlCommand(sql, connection);
        var result = new List<DbPublication>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbPublication
            {
                Name = reader.GetString(0),
                Tables = reader.IsDBNull(1) ? null : reader.GetString(1),
                Definition = reader.GetString(2)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DbSubscription>> GetSubscriptionsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT s.subname AS subscription_name,
                   s.subpublications[1] AS publication,
                   s.subconninfo AS connection_string,
                   'CREATE SUBSCRIPTION ' || quote_ident(s.subname) ||
                   ' CONNECTION ' || quote_literal(COALESCE(s.subconninfo, '')) ||
                   ' PUBLICATION ' || array_to_string(ARRAY(
                       SELECT quote_ident(x) FROM unnest(s.subpublications) AS x), ', ') ||
                   ';' AS definition
            FROM pg_subscription s
            WHERE s.subdbid = (SELECT oid FROM pg_database WHERE datname = current_database())
            ORDER BY s.subname;";

        var result = new List<DbSubscription>();

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new DbSubscription
                {
                    Name = reader.GetString(0),
                    Publication = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ConnectionString = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Definition = reader.GetString(3)
                });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            // pg_subscription.subconninfo is restricted to superusers.
            // Degrade gracefully rather than failing the entire export.
        }

        return result;
    }

    private static void AddSchemaParameters(NpgsqlCommand command, ExportOptions options)
    {
        command.Parameters.AddWithValue("schemas", options.Schemas);
        command.Parameters.AddWithValue("excludeSchemas", options.ExcludeSchemas);
    }

    private static string NormalizeDataType(string dataType, string udtSchema, string udtName)
    {
        if (dataType == "ARRAY")
            return $"{udtName.TrimStart('_')}[]";

        if (dataType == "USER-DEFINED")
        {
            if (udtSchema is "pg_catalog" or "public")
                return udtName;

            return $"{udtSchema}.{udtName}";
        }

        return dataType;
    }
}
