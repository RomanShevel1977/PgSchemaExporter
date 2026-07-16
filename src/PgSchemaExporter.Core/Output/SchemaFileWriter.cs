using System.Security.Cryptography;
using System.Text;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Options;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Output;

public sealed class SchemaFileWriter
{
    private readonly ExtensionScriptGenerator _extensionGenerator = new();
    private readonly SchemaScriptGenerator _schemaGenerator = new();
    private readonly TypeScriptGenerator _typeGenerator = new();
    private readonly SequenceScriptGenerator _sequenceGenerator = new();
    private readonly DomainScriptGenerator _domainGenerator = new();
    private readonly ForeignTableScriptGenerator _foreignTableGenerator = new();
    private readonly TableScriptGenerator _tableGenerator = new();
    private readonly ConstraintScriptGenerator _constraintGenerator = new();
    private readonly IndexScriptGenerator _indexGenerator = new();
    private readonly ViewScriptGenerator _viewGenerator = new();
    private readonly FunctionScriptGenerator _functionGenerator = new();
    private readonly EventTriggerScriptGenerator _eventTriggerGenerator = new();
    private readonly RuleScriptGenerator _ruleGenerator = new();
    private readonly AggregateScriptGenerator _aggregateGenerator = new();
    private readonly OperatorScriptGenerator _operatorGenerator = new();
    private readonly CastScriptGenerator _castGenerator = new();
    private readonly PublicationScriptGenerator _publicationGenerator = new();
    private readonly SubscriptionScriptGenerator _subscriptionGenerator = new();

    public Task<FileWriteResult> WriteAsync(
        string outputDirectory,
        DatabaseModel model,
        CancellationToken cancellationToken = default)
        => WriteAsync(outputDirectory, model, new FormatOptions(), cancellationToken);

    public async Task<FileWriteResult> WriteAsync(
        string outputDirectory,
        DatabaseModel model,
        FormatOptions format,
        CancellationToken cancellationToken = default)
    {
        var result = new FileWriteResult();

        if (model.Extensions.Any())
        {
            var sql = string.Join(Environment.NewLine, model.Extensions.OrderBy(x => x.Name).Select(_extensionGenerator.Generate));
            result.ExtensionFiles.Add(await WriteFileAsync(outputDirectory, "extensions", "001_extensions.sql", ApplyFormat(sql, format), cancellationToken));
        }

        foreach (var item in model.Schemas.OrderBy(x => x.Name))
            result.SchemaFiles.Add(await WriteFileAsync(outputDirectory, "schemas", $"{Safe(item.Name)}.sql", ApplyFormat(_schemaGenerator.Generate(item), format), cancellationToken));

        foreach (var item in model.Types.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.TypeFiles.Add(await WriteFileAsync(outputDirectory, "types", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", ApplyFormat(_typeGenerator.Generate(item), format), cancellationToken));

        foreach (var item in model.Sequences.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.SequenceFiles.Add(await WriteFileAsync(outputDirectory, "sequences", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", ApplyFormat(_sequenceGenerator.Generate(item), format), cancellationToken));

        foreach (var item in model.Domains.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.DomainFiles.Add(await WriteFileAsync(outputDirectory, "domains", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", ApplyFormat(_domainGenerator.Generate(item), format), cancellationToken));

        foreach (var item in model.ForeignTables.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.ForeignTableFiles.Add(await WriteFileAsync(outputDirectory, "foreign_tables", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", ApplyFormat(_foreignTableGenerator.Generate(item), format), cancellationToken));

        var tableKeys = new HashSet<string>(model.Tables.Select(t => TableKey(t.Schema, t.Name)));

        var constraintsByTable = model.Constraints
            .GroupBy(x => TableKey(x.Schema, x.TableName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var indexesByTable = model.Indexes
            .GroupBy(x => TableKey(x.Schema, x.TableName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var tables = model.Tables.OrderBy(x => x.Schema).ThenBy(x => x.Name).ToList();

        await WriteItemsParallelAsync(
            outputDirectory,
            "tables",
            tables,
            item =>
            {
                var sql = ApplyFormat(_tableGenerator.Generate(item), format);
                var key = TableKey(item.Schema, item.Name);

                if (!format.SplitConstraints && constraintsByTable.TryGetValue(key, out var inlineConstraints))
                {
                    sql += Environment.NewLine + ApplyFormat(
                        string.Join(Environment.NewLine, inlineConstraints.Select(_constraintGenerator.Generate)), format);
                }

                if (!format.SplitIndexes && indexesByTable.TryGetValue(key, out var inlineIndexes))
                {
                    sql += Environment.NewLine + ApplyFormat(
                        string.Join(Environment.NewLine, inlineIndexes.Select(_indexGenerator.Generate)), format);
                }

                return ($"{Safe(item.Schema)}.{Safe(item.Name)}.sql", sql);
            },
            result.TableFiles,
            cancellationToken);

        var constraintGroups = model.Constraints
            .GroupBy(x => new { x.Schema, x.TableName })
            .Where(g => format.SplitConstraints || !tableKeys.Contains(TableKey(g.Key.Schema, g.Key.TableName)))
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.TableName)
            .ToList();

        await WriteItemsParallelAsync(
            outputDirectory,
            "constraints",
            constraintGroups,
            group =>
            {
                var sql = ApplyFormat(string.Join(Environment.NewLine, group.Select(_constraintGenerator.Generate)), format);
                return ($"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.constraints.sql", sql);
            },
            result.ConstraintFiles,
            cancellationToken);

        var indexGroups = model.Indexes
            .GroupBy(x => new { x.Schema, x.TableName })
            .Where(g => format.SplitIndexes || !tableKeys.Contains(TableKey(g.Key.Schema, g.Key.TableName)))
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.TableName)
            .ToList();

        await WriteItemsParallelAsync(
            outputDirectory,
            "indexes",
            indexGroups,
            group =>
            {
                var sql = ApplyFormat(string.Join(Environment.NewLine, group.Select(_indexGenerator.Generate)), format);
                return ($"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.indexes.sql", sql);
            },
            result.IndexFiles,
            cancellationToken);

        foreach (var item in model.Views.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.ViewFiles.Add(await WriteFileAsync(outputDirectory, "views", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", _viewGenerator.Generate(item), cancellationToken));

        foreach (var trigger in model.Triggers.OrderBy(x => x.Schema).ThenBy(x => x.TableName).ThenBy(x => x.Name))
            result.TriggerFiles.Add(await WriteFileAsync(outputDirectory, "triggers", $"{Safe(trigger.Schema)}.{Safe(trigger.TableName)}.{Safe(trigger.Name)}.sql", trigger.Definition, cancellationToken));

        foreach (var eventTrigger in model.EventTriggers.OrderBy(x => x.Name))
            result.EventTriggerFiles.Add(await WriteFileAsync(outputDirectory, "event_triggers", $"{Safe(eventTrigger.Name)}.sql", ApplyFormat(_eventTriggerGenerator.Generate(eventTrigger), new FormatOptions()), cancellationToken));

        foreach (var rule in model.Rules.OrderBy(x => x.Schema).ThenBy(x => x.TableName).ThenBy(x => x.Name))
            result.RuleFiles.Add(await WriteFileAsync(outputDirectory, "rules", $"{Safe(rule.Schema)}.{Safe(rule.TableName)}.{Safe(rule.Name)}.sql", ApplyFormat(_ruleGenerator.Generate(rule), new FormatOptions()), cancellationToken));

        foreach (var aggregate in model.Aggregates.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.AggregateFiles.Add(await WriteFileAsync(outputDirectory, "aggregates", $"{Safe(aggregate.Schema)}.{Safe(aggregate.Name)}.sql", ApplyFormat(_aggregateGenerator.Generate(aggregate), new FormatOptions()), cancellationToken));

        foreach (var op in model.Operators.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.OperatorFiles.Add(await WriteFileAsync(outputDirectory, "operators", $"{Safe(op.Schema)}.{Safe(op.Name)}.sql", ApplyFormat(_operatorGenerator.Generate(op), new FormatOptions()), cancellationToken));

        foreach (var cast in model.Casts.OrderBy(x => x.SourceType).ThenBy(x => x.TargetType))
            result.CastFiles.Add(await WriteFileAsync(outputDirectory, "casts", $"{Safe(cast.SourceType)}_to_{Safe(cast.TargetType)}.sql", ApplyFormat(_castGenerator.Generate(cast), new FormatOptions()), cancellationToken));

        foreach (var publication in model.Publications.OrderBy(x => x.Name))
            result.PublicationFiles.Add(await WriteFileAsync(outputDirectory, "publications", $"{Safe(publication.Name)}.sql", ApplyFormat(_publicationGenerator.Generate(publication), new FormatOptions()), cancellationToken));

        foreach (var subscription in model.Subscriptions.OrderBy(x => x.Name))
            result.SubscriptionFiles.Add(await WriteFileAsync(outputDirectory, "subscriptions", $"{Safe(subscription.Name)}.sql", ApplyFormat(_subscriptionGenerator.Generate(subscription), new FormatOptions()), cancellationToken));

        foreach (var policy in model.Policies.OrderBy(x => x.Schema).ThenBy(x => x.TableName).ThenBy(x => x.Name))
            result.PolicyFiles.Add(await WriteFileAsync(outputDirectory, "policies", $"{Safe(policy.Schema)}.{Safe(policy.TableName)}.{Safe(policy.Name)}.sql", policy.Definition, cancellationToken));

        foreach (var group in model.Comments
            .GroupBy(x => new { x.Schema, x.ObjectType, x.ObjectName, x.SubObject })
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.ObjectType)
            .ThenBy(x => x.Key.ObjectName)
            .ThenBy(x => x.Key.SubObject))
        {
            var fileName = group.Key.SubObject is null
                ? $"{Safe(group.Key.Schema)}.{Safe(group.Key.ObjectType)}.{Safe(group.Key.ObjectName)}.sql"
                : $"{Safe(group.Key.Schema)}.{Safe(group.Key.ObjectType)}.{Safe(group.Key.ObjectName)}.{Safe(group.Key.SubObject)}.sql";
            var sql = string.Join(Environment.NewLine, group.Select(x => x.Definition));
            result.CommentFiles.Add(await WriteFileAsync(outputDirectory, "comments", fileName, sql, cancellationToken));
        }

        foreach (var group in model.Grants
            .GroupBy(x => new { x.Schema, x.ObjectType, x.ObjectName, x.SubObject })
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.ObjectType)
            .ThenBy(x => x.Key.ObjectName)
            .ThenBy(x => x.Key.SubObject))
        {
            var fileName = group.Key.SubObject is null
                ? $"{Safe(group.Key.Schema)}.{Safe(group.Key.ObjectType)}.{Safe(group.Key.ObjectName)}.sql"
                : $"{Safe(group.Key.Schema)}.{Safe(group.Key.ObjectType)}.{Safe(group.Key.ObjectName)}.{Safe(group.Key.SubObject)}.sql";
            var sql = string.Join(Environment.NewLine, group.Select(x => x.Definition));
            result.GrantFiles.Add(await WriteFileAsync(outputDirectory, "grants", fileName, sql, cancellationToken));
        }

        var functions = model.Functions.OrderBy(x => x.Schema).ThenBy(x => x.Name).ThenBy(x => x.ArgumentsIdentity).ToList();

        await WriteItemsParallelAsync(
            outputDirectory,
            "functions",
            functions,
            item =>
            {
                var argsHash = StableHash(item.ArgumentsIdentity);
                return ($"{Safe(item.Schema)}.{Safe(item.Name)}.{argsHash}.sql", _functionGenerator.Generate(item));
            },
            result.FunctionFiles,
            cancellationToken);

        return result;
    }

    private static async Task<string> WriteFileAsync(
        string outputDirectory,
        string folder,
        string fileName,
        string content,
        CancellationToken cancellationToken)
    {
        var dir = Path.Combine(outputDirectory, folder);
        Directory.CreateDirectory(dir);

        var fullPath = Path.Combine(dir, fileName);
        var relativePath = $"{folder}/{fileName}";

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return relativePath;
    }

    private static async Task WriteItemsParallelAsync<T>(
        string outputDirectory,
        string folder,
        IReadOnlyList<T> items,
        Func<T, (string fileName, string content)> generator,
        List<string> result,
        CancellationToken cancellationToken)
    {
        var entries = new (string fileName, string content)[items.Count];
        Parallel.For(0, items.Count, i => entries[i] = generator(items[i]));

        foreach (var (fileName, content) in entries)
        {
            var relativePath = await WriteFileAsync(outputDirectory, folder, fileName, content, cancellationToken);
            result.Add(relativePath);
        }
    }

    private static string Safe(string value) => SqlIdentifier.SafeFileName(value);

    private static string TableKey(string schema, string table) => $"{schema}\u0000{table}";

    // Deterministic hash so the same function always maps to the same file name across runs,
    // keeping git diffs clean (string.GetHashCode is randomized per process).
    private static string StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    private static string ApplyFormat(string sql, FormatOptions format)
    {
        if (!format.UseIfNotExists)
            sql = sql.Replace(" IF NOT EXISTS ", " ", StringComparison.OrdinalIgnoreCase);

        return sql;
    }
}
