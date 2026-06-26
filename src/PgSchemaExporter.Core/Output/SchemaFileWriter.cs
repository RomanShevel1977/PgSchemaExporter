using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Output;

public sealed class SchemaFileWriter
{
    private readonly ExtensionScriptGenerator _extensionGenerator = new();
    private readonly SchemaScriptGenerator _schemaGenerator = new();
    private readonly TypeScriptGenerator _typeGenerator = new();
    private readonly SequenceScriptGenerator _sequenceGenerator = new();
    private readonly TableScriptGenerator _tableGenerator = new();
    private readonly ConstraintScriptGenerator _constraintGenerator = new();
    private readonly IndexScriptGenerator _indexGenerator = new();
    private readonly ViewScriptGenerator _viewGenerator = new();
    private readonly FunctionScriptGenerator _functionGenerator = new();

    public async Task<FileWriteResult> WriteAsync(
        string outputDirectory,
        DatabaseModel model,
        CancellationToken cancellationToken = default)
    {
        var result = new FileWriteResult();

        if (model.Extensions.Any())
        {
            var sql = string.Join(Environment.NewLine, model.Extensions.OrderBy(x => x.Name).Select(_extensionGenerator.Generate));
            result.ExtensionFiles.Add(await WriteFileAsync(outputDirectory, "extensions", "001_extensions.sql", sql, cancellationToken));
        }

        foreach (var item in model.Schemas.OrderBy(x => x.Name))
            result.SchemaFiles.Add(await WriteFileAsync(outputDirectory, "schemas", $"{Safe(item.Name)}.sql", _schemaGenerator.Generate(item), cancellationToken));

        foreach (var item in model.Types.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.TypeFiles.Add(await WriteFileAsync(outputDirectory, "types", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", _typeGenerator.Generate(item), cancellationToken));

        foreach (var item in model.Sequences.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.SequenceFiles.Add(await WriteFileAsync(outputDirectory, "sequences", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", _sequenceGenerator.Generate(item), cancellationToken));

        foreach (var item in model.Tables.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.TableFiles.Add(await WriteFileAsync(outputDirectory, "tables", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", _tableGenerator.Generate(item), cancellationToken));

        foreach (var group in model.Constraints.GroupBy(x => new { x.Schema, x.TableName }).OrderBy(x => x.Key.Schema).ThenBy(x => x.Key.TableName))
        {
            var sql = string.Join(Environment.NewLine, group.Select(_constraintGenerator.Generate));
            result.ConstraintFiles.Add(await WriteFileAsync(outputDirectory, "constraints", $"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.constraints.sql", sql, cancellationToken));
        }

        foreach (var group in model.Indexes.GroupBy(x => new { x.Schema, x.TableName }).OrderBy(x => x.Key.Schema).ThenBy(x => x.Key.TableName))
        {
            var sql = string.Join(Environment.NewLine, group.Select(_indexGenerator.Generate));
            result.IndexFiles.Add(await WriteFileAsync(outputDirectory, "indexes", $"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.indexes.sql", sql, cancellationToken));
        }

        foreach (var item in model.Views.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            result.ViewFiles.Add(await WriteFileAsync(outputDirectory, "views", $"{Safe(item.Schema)}.{Safe(item.Name)}.sql", _viewGenerator.Generate(item), cancellationToken));

        foreach (var trigger in model.Triggers.OrderBy(x => x.Schema).ThenBy(x => x.TableName).ThenBy(x => x.Name))
            result.TriggerFiles.Add(await WriteFileAsync(outputDirectory, "triggers", $"{Safe(trigger.Schema)}.{Safe(trigger.TableName)}.{Safe(trigger.Name)}.sql", trigger.Definition, cancellationToken));

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

        foreach (var item in model.Functions.OrderBy(x => x.Schema).ThenBy(x => x.Name).ThenBy(x => x.ArgumentsIdentity))
        {
            var argsHash = Math.Abs(item.ArgumentsIdentity.GetHashCode()).ToString("x");
            result.FunctionFiles.Add(await WriteFileAsync(outputDirectory, "functions", $"{Safe(item.Schema)}.{Safe(item.Name)}.{argsHash}.sql", _functionGenerator.Generate(item), cancellationToken));
        }

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

    private static string Safe(string value) => SqlIdentifier.SafeFileName(value);
}
