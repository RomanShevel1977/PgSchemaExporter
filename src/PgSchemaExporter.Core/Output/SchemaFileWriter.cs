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
            var sql = string.Join(
                Environment.NewLine,
                model.Extensions
                    .OrderBy(x => x.Name)
                    .Select(_extensionGenerator.Generate));

            result.ExtensionFiles.Add(await WriteFileAsync(
                outputDirectory,
                "extensions",
                "001_extensions.sql",
                sql,
                cancellationToken));
        }

        foreach (var item in model.Schemas.OrderBy(x => x.Name))
        {
            result.SchemaFiles.Add(await WriteFileAsync(
                outputDirectory,
                "schemas",
                $"{Safe(item.Name)}.sql",
                _schemaGenerator.Generate(item),
                cancellationToken));
        }

        foreach (var item in model.Types.OrderBy(x => x.Schema).ThenBy(x => x.Name))
        {
            result.TypeFiles.Add(await WriteFileAsync(
                outputDirectory,
                "types",
                $"{Safe(item.Schema)}.{Safe(item.Name)}.sql",
                _typeGenerator.Generate(item),
                cancellationToken));
        }

        foreach (var item in model.Sequences.OrderBy(x => x.Schema).ThenBy(x => x.Name))
        {
            result.SequenceFiles.Add(await WriteFileAsync(
                outputDirectory,
                "sequences",
                $"{Safe(item.Schema)}.{Safe(item.Name)}.sql",
                _sequenceGenerator.Generate(item),
                cancellationToken));
        }

        foreach (var item in model.Tables.OrderBy(x => x.Schema).ThenBy(x => x.Name))
        {
            result.TableFiles.Add(await WriteFileAsync(
                outputDirectory,
                "tables",
                $"{Safe(item.Schema)}.{Safe(item.Name)}.sql",
                _tableGenerator.Generate(item),
                cancellationToken));
        }

        /*
         * Important:
         * Foreign keys must be created only after referenced PRIMARY KEY / UNIQUE
         * constraints already exist. Therefore constraints are written in two phases:
         *
         * 1. PRIMARY KEY / UNIQUE / CHECK constraints
         * 2. FOREIGN KEY constraints
         */
        var keyConstraints = model.Constraints
            .Where(x => !IsForeignKey(x))
            .GroupBy(x => new { x.Schema, x.TableName })
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.TableName);

        foreach (var group in keyConstraints)
        {
            var sql = string.Join(
                Environment.NewLine,
                group.Select(_constraintGenerator.Generate));

            result.ConstraintFiles.Add(await WriteFileAsync(
                outputDirectory,
                "constraints/keys",
                $"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.constraints.sql",
                sql,
                cancellationToken));
        }

        var foreignKeyConstraints = model.Constraints
            .Where(IsForeignKey)
            .GroupBy(x => new { x.Schema, x.TableName })
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.TableName);

        foreach (var group in foreignKeyConstraints)
        {
            var sql = string.Join(
                Environment.NewLine,
                group.Select(_constraintGenerator.Generate));

            result.ConstraintFiles.Add(await WriteFileAsync(
                outputDirectory,
                "constraints/foreign_keys",
                $"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.foreign_keys.sql",
                sql,
                cancellationToken));
        }

        foreach (var group in model.Indexes
                     .GroupBy(x => new { x.Schema, x.TableName })
                     .OrderBy(x => x.Key.Schema)
                     .ThenBy(x => x.Key.TableName))
        {
            var sql = string.Join(
                Environment.NewLine,
                group.Select(_indexGenerator.Generate));

            result.IndexFiles.Add(await WriteFileAsync(
                outputDirectory,
                "indexes",
                $"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.indexes.sql",
                sql,
                cancellationToken));
        }

        foreach (var item in model.Views.OrderBy(x => x.Schema).ThenBy(x => x.Name))
        {
            result.ViewFiles.Add(await WriteFileAsync(
                outputDirectory,
                "views",
                $"{Safe(item.Schema)}.{Safe(item.Name)}.sql",
                _viewGenerator.Generate(item),
                cancellationToken));
        }

        foreach (var item in model.Functions
                     .OrderBy(x => x.Schema)
                     .ThenBy(x => x.Name)
                     .ThenBy(x => x.ArgumentsIdentity))
        {
            var argsHash = Math.Abs(item.ArgumentsIdentity.GetHashCode()).ToString("x");

            result.FunctionFiles.Add(await WriteFileAsync(
                outputDirectory,
                "functions",
                $"{Safe(item.Schema)}.{Safe(item.Name)}.{argsHash}.sql",
                _functionGenerator.Generate(item),
                cancellationToken));
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
        var relativePath = $"{folder.Replace('\\', '/')}/{fileName}";

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);

        return relativePath;
    }

    private static bool IsForeignKey(DbConstraint constraint)
    {
        return constraint.Type.Equals("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
            || constraint.Definition.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase);
    }

    private static string Safe(string value) => SqlIdentifier.SafeFileName(value);
}