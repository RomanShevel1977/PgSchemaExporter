using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Output;

public sealed class DumpSplitFileWriter
{
    public async Task<FileWriteResult> WriteAsync(
        string outputDirectory,
        IReadOnlyList<SqlDumpObject> objects,
        CancellationToken cancellationToken = default)
    {
        var result = new FileWriteResult();

        foreach (var group in objects.GroupBy(x => x.Type))
        {
            switch (group.Key)
            {
                case SqlObjectType.Extension:
                    result.ExtensionFiles.AddRange(await WriteObjectsAsync(outputDirectory, "extensions", group, cancellationToken));
                    break;
                case SqlObjectType.Schema:
                    result.SchemaFiles.AddRange(await WriteObjectsAsync(outputDirectory, "schemas", group, cancellationToken));
                    break;
                case SqlObjectType.Type:
                    result.TypeFiles.AddRange(await WriteObjectsAsync(outputDirectory, "types", group, cancellationToken));
                    break;
                case SqlObjectType.Sequence:
                    result.SequenceFiles.AddRange(await WriteObjectsAsync(outputDirectory, "sequences", group, cancellationToken));
                    break;
                case SqlObjectType.Table:
                    result.TableFiles.AddRange(await WriteObjectsAsync(outputDirectory, "tables", group, cancellationToken));
                    break;
                case SqlObjectType.Constraint:
                    result.ConstraintFiles.AddRange(await WriteGroupedByParentAsync(outputDirectory, "constraints", group, "constraints", cancellationToken));
                    break;
                case SqlObjectType.Index:
                    result.IndexFiles.AddRange(await WriteGroupedByParentAsync(outputDirectory, "indexes", group, "indexes", cancellationToken));
                    break;
                case SqlObjectType.View:
                    result.ViewFiles.AddRange(await WriteObjectsAsync(outputDirectory, "views", group, cancellationToken));
                    break;
                case SqlObjectType.Function:
                    result.FunctionFiles.AddRange(await WriteObjectsAsync(outputDirectory, "functions", group, cancellationToken));
                    break;
                case SqlObjectType.Trigger:
                    result.TriggerFiles.AddRange(await WriteObjectsAsync(outputDirectory, "triggers", group, cancellationToken));
                    break;
                case SqlObjectType.Policy:
                    result.PolicyFiles.AddRange(await WriteObjectsAsync(outputDirectory, "policies", group, cancellationToken));
                    break;
                case SqlObjectType.Comment:
                    result.CommentFiles.AddRange(await WriteObjectsAsync(outputDirectory, "comments", group, cancellationToken));
                    break;
                case SqlObjectType.Grant:
                    result.GrantFiles.AddRange(await WriteObjectsAsync(outputDirectory, "grants", group, cancellationToken));
                    break;
                default:
                    result.OtherFiles.AddRange(await WriteObjectsAsync(outputDirectory, "misc", group, cancellationToken));
                    break;
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> WriteObjectsAsync(
        string outputDirectory,
        string folder,
        IEnumerable<SqlDumpObject> objects,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        foreach (var obj in objects.OrderBy(x => x.Schema).ThenBy(x => x.Name).ThenBy(x => x.Order))
        {
            var fileName = $"{Safe(obj.Schema)}.{Safe(obj.Name)}.sql";
            files.Add(await WriteFileAsync(outputDirectory, folder, fileName, EnsureSemicolon(obj.Statement), cancellationToken));
        }

        return files;
    }

    private static async Task<IReadOnlyList<string>> WriteGroupedByParentAsync(
        string outputDirectory,
        string folder,
        IEnumerable<SqlDumpObject> objects,
        string suffix,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        var groups = objects
            .GroupBy(x => new { x.Schema, Parent = x.ParentName ?? x.Name })
            .OrderBy(x => x.Key.Schema)
            .ThenBy(x => x.Key.Parent);

        foreach (var group in groups)
        {
            var fileName = $"{Safe(group.Key.Schema)}.{Safe(group.Key.Parent)}.{suffix}.sql";
            var sql = string.Join(
                Environment.NewLine + Environment.NewLine,
                group.OrderBy(x => x.Order).Select(x => EnsureSemicolon(x.Statement)));

            files.Add(await WriteFileAsync(outputDirectory, folder, fileName, sql, cancellationToken));
        }

        return files;
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

        await File.WriteAllTextAsync(fullPath, content.TrimEnd() + Environment.NewLine, cancellationToken);
        return relativePath;
    }

    private static string EnsureSemicolon(string sql)
    {
        sql = sql.Trim();
        return sql.EndsWith(';') ? sql : sql + ";";
    }

    private static string Safe(string value)
    {
        return SqlIdentifier.SafeFileName(value);
    }
}
