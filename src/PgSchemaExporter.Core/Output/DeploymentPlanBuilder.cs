using System.Text.RegularExpressions;
using PgSchemaExporter.Core.Models;
using PgSchemaExporter.Core.Scripting;

namespace PgSchemaExporter.Core.Output;

public sealed class DeploymentPlanBuilder
{
    public DeploymentPlan Build(DatabaseModel model, FileWriteResult files)
    {
        var allFiles = files.GetDeployOrder();
        var dependencies = allFiles.ToDictionary(x => x, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var schemaFiles = files.SchemaFiles.ToDictionary(GetSchemaNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var extensionFiles = files.ExtensionFiles.ToList();
        var typeFiles = files.TypeFiles.ToDictionary(GetSchemaQualifiedNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var sequenceFiles = files.SequenceFiles.ToDictionary(GetSchemaQualifiedNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var domainFiles = files.DomainFiles.ToDictionary(GetSchemaQualifiedNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var foreignTableFiles = files.ForeignTableFiles.ToDictionary(GetSchemaQualifiedNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var tableFiles = files.TableFiles.ToDictionary(GetSchemaQualifiedNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var viewFiles = files.ViewFiles.ToDictionary(GetSchemaQualifiedNameFromFile, x => x, StringComparer.OrdinalIgnoreCase);
        var functionFiles = files.FunctionFiles.ToList();

        foreach (var extension in model.Extensions)
        {
            // There is normally a single extensions file. If an extension is installed into a custom schema,
            // that schema must exist before CREATE EXTENSION ... WITH SCHEMA can run.
            foreach (var extensionFile in extensionFiles)
                AddDependency(dependencies, extensionFile, TryGetSchemaFile(schemaFiles, extension.Schema));
        }

        foreach (var type in model.Types)
            AddDependency(dependencies, TryGet(typeFiles, type.Schema, type.Name), TryGetSchemaFile(schemaFiles, type.Schema));

        foreach (var sequence in model.Sequences)
            AddDependency(dependencies, TryGet(sequenceFiles, sequence.Schema, sequence.Name), TryGetSchemaFile(schemaFiles, sequence.Schema));

        foreach (var domain in model.Domains)
            AddDependency(dependencies, TryGet(domainFiles, domain.Schema, domain.Name), TryGetSchemaFile(schemaFiles, domain.Schema));

        foreach (var foreignTable in model.ForeignTables)
            AddDependency(dependencies, TryGet(foreignTableFiles, foreignTable.Schema, foreignTable.Name), TryGetSchemaFile(schemaFiles, foreignTable.Schema));

        foreach (var table in model.Tables)
        {
            var tableFile = TryGet(tableFiles, table.Schema, table.Name);
            AddDependency(dependencies, tableFile, TryGetSchemaFile(schemaFiles, table.Schema));

            foreach (var column in table.Columns)
            {
                foreach (var type in model.Types)
                {
                    if (ReferencesObject(column.DataType, type.Schema, type.Name))
                        AddDependency(dependencies, tableFile, TryGet(typeFiles, type.Schema, type.Name));
                }

                if (!string.IsNullOrWhiteSpace(column.DefaultValue))
                {
                    foreach (var sequence in model.Sequences)
                    {
                        if (ReferencesObject(column.DefaultValue!, sequence.Schema, sequence.Name))
                            AddDependency(dependencies, tableFile, TryGet(sequenceFiles, sequence.Schema, sequence.Name));
                    }
                }
            }
        }

        foreach (var group in model.Constraints.GroupBy(x => new { x.Schema, x.TableName }))
        {
            var constraintFile = files.ConstraintFiles.FirstOrDefault(x => x.StartsWith($"constraints/{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.", StringComparison.OrdinalIgnoreCase));
            var tableFile = TryGet(tableFiles, group.Key.Schema, group.Key.TableName);
            AddDependency(dependencies, constraintFile, tableFile);

            foreach (var constraint in group)
            {
                foreach (var table in model.Tables)
                {
                    if (ReferencesTable(constraint.Definition, table.Schema, table.Name))
                    {
                        // If constraint references another table (e.g., FOREIGN KEY), 
                        // depend on that table's constraint file, not just the table
                        var referencedConstraintFile = files.ConstraintFiles.FirstOrDefault(x =>
                            x.StartsWith($"constraints/{Safe(table.Schema)}.{Safe(table.Name)}.", StringComparison.OrdinalIgnoreCase));
                        AddDependency(dependencies, constraintFile, referencedConstraintFile ?? TryGet(tableFiles, table.Schema, table.Name));
                    }
                }
            }
        }

        foreach (var group in model.Indexes.GroupBy(x => new { x.Schema, x.TableName }))
        {
            var indexFile = files.IndexFiles.FirstOrDefault(x => x.StartsWith($"indexes/{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}.", StringComparison.OrdinalIgnoreCase));
            AddDependency(dependencies, indexFile, TryGet(tableFiles, group.Key.Schema, group.Key.TableName));
        }

        foreach (var function in model.Functions)
        {
            var functionFile = functionFiles.FirstOrDefault(x => x.StartsWith($"functions/{Safe(function.Schema)}.{Safe(function.Name)}.", StringComparison.OrdinalIgnoreCase));
            AddDependency(dependencies, functionFile, TryGetSchemaFile(schemaFiles, function.Schema));

            foreach (var type in model.Types)
            {
                if (ReferencesObject(function.Definition, type.Schema, type.Name))
                    AddDependency(dependencies, functionFile, TryGet(typeFiles, type.Schema, type.Name));
            }

            foreach (var table in model.Tables)
            {
                if (ReferencesObject(function.Definition, table.Schema, table.Name))
                    AddDependency(dependencies, functionFile, TryGet(tableFiles, table.Schema, table.Name));
            }

            foreach (var otherFunction in model.Functions.Where(x =>
                !(x.Schema.Equals(function.Schema, StringComparison.OrdinalIgnoreCase) &&
                  x.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase))))
            {
                if (ReferencesRoutine(function.Definition, otherFunction.Schema, otherFunction.Name))
                {
                    var otherFunctionFile = functionFiles.FirstOrDefault(x =>
                        x.StartsWith($"functions/{Safe(otherFunction.Schema)}.{Safe(otherFunction.Name)}.",
                            StringComparison.OrdinalIgnoreCase));

                    AddDependency(dependencies, functionFile, otherFunctionFile);
                }
            }

            foreach (var view in model.Views)
            {
                if (ReferencesObject(function.Definition, view.Schema, view.Name))
                    AddDependency(dependencies, functionFile, TryGet(viewFiles, view.Schema, view.Name));
            }
        }

        foreach (var view in model.Views)
        {
            var viewFile = TryGet(viewFiles, view.Schema, view.Name);
            AddDependency(dependencies, viewFile, TryGetSchemaFile(schemaFiles, view.Schema));

            foreach (var table in model.Tables)
            {
                if (ReferencesObject(view.Definition, table.Schema, table.Name))
                    AddDependency(dependencies, viewFile, TryGet(tableFiles, table.Schema, table.Name));
            }

            foreach (var otherView in model.Views.Where(x => !(x.Schema.Equals(view.Schema, StringComparison.OrdinalIgnoreCase) && x.Name.Equals(view.Name, StringComparison.OrdinalIgnoreCase))))
            {
                if (ReferencesObject(view.Definition, otherView.Schema, otherView.Name))
                    AddDependency(dependencies, viewFile, TryGet(viewFiles, otherView.Schema, otherView.Name));
            }

            foreach (var function in model.Functions)
            {
                if (ReferencesObject(view.Definition, function.Schema, function.Name))
                {
                    var functionFile = functionFiles.FirstOrDefault(x => x.StartsWith($"functions/{Safe(function.Schema)}.{Safe(function.Name)}.", StringComparison.OrdinalIgnoreCase));
                    AddDependency(dependencies, viewFile, functionFile);
                }
            }
        }

        foreach (var trigger in model.Triggers)
        {
            var triggerFile = files.TriggerFiles.FirstOrDefault(x => x.StartsWith($"triggers/{Safe(trigger.Schema)}.{Safe(trigger.TableName)}.{Safe(trigger.Name)}.", StringComparison.OrdinalIgnoreCase));
            AddDependency(dependencies, triggerFile, TryGet(tableFiles, trigger.TableSchema, trigger.TableName));

            foreach (var function in model.Functions)
            {
                if (ReferencesRoutine(trigger.Definition, function.Schema, function.Name))
                {
                    var functionFile = functionFiles.FirstOrDefault(x => x.StartsWith($"functions/{Safe(function.Schema)}.{Safe(function.Name)}.", StringComparison.OrdinalIgnoreCase));
                    AddDependency(dependencies, triggerFile, functionFile);
                }
            }
        }

        foreach (var policy in model.Policies)
        {
            var policyFile = files.PolicyFiles.FirstOrDefault(x => x.StartsWith($"policies/{Safe(policy.Schema)}.{Safe(policy.TableName)}.{Safe(policy.Name)}.", StringComparison.OrdinalIgnoreCase));
            AddDependency(dependencies, policyFile, TryGet(tableFiles, policy.TableSchema, policy.TableName));

            foreach (var function in model.Functions)
            {
                if (ReferencesRoutine(policy.Definition, function.Schema, function.Name))
                {
                    var functionFile = functionFiles.FirstOrDefault(x => x.StartsWith($"functions/{Safe(function.Schema)}.{Safe(function.Name)}.", StringComparison.OrdinalIgnoreCase));
                    AddDependency(dependencies, policyFile, functionFile);
                }
            }
        }

        return BuildPlan(allFiles, dependencies);
    }

    public DeploymentPlan Build(FileWriteResult files)
    {
        var allFiles = files.GetDeployOrder();
        var dependencies = allFiles.ToDictionary(x => x, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        AddFolderDependencies(files.ExtensionFiles, files.SchemaFiles, dependencies);
        AddFolderDependencies(files.TypeFiles, files.SchemaFiles.Concat(files.ExtensionFiles), dependencies);
        AddFolderDependencies(files.SequenceFiles, files.SchemaFiles, dependencies);
        AddFolderDependencies(files.DomainFiles, files.SchemaFiles.Concat(files.TypeFiles).Concat(files.SequenceFiles), dependencies);
        AddFolderDependencies(files.ForeignTableFiles, files.SchemaFiles.Concat(files.TypeFiles).Concat(files.TableFiles), dependencies);
        AddFolderDependencies(files.TableFiles, files.SchemaFiles.Concat(files.TypeFiles).Concat(files.SequenceFiles), dependencies);
        AddFolderDependencies(files.ConstraintFiles, files.TableFiles, dependencies);
        AddFolderDependencies(files.IndexFiles, files.TableFiles, dependencies);
        AddFolderDependencies(files.FunctionFiles, files.SchemaFiles.Concat(files.TypeFiles).Concat(files.TableFiles), dependencies);
        AddFolderDependencies(files.ViewFiles, files.TableFiles.Concat(files.FunctionFiles), dependencies);
        AddFolderDependencies(files.TriggerFiles, files.TableFiles.Concat(files.FunctionFiles), dependencies);
        AddFolderDependencies(files.PolicyFiles, files.TableFiles.Concat(files.FunctionFiles), dependencies);

        var objectFiles = files.ExtensionFiles
            .Concat(files.SchemaFiles)
            .Concat(files.TypeFiles)
            .Concat(files.SequenceFiles)
            .Concat(files.TableFiles)
            .Concat(files.ConstraintFiles)
            .Concat(files.IndexFiles)
            .Concat(files.FunctionFiles)
            .Concat(files.ViewFiles)
            .Concat(files.TriggerFiles)
            .Concat(files.PolicyFiles)
            .ToList();

        AddFolderDependencies(files.CommentFiles, objectFiles, dependencies);
        AddFolderDependencies(files.GrantFiles, objectFiles, dependencies);
        AddFolderDependencies(files.OtherFiles, objectFiles, dependencies);

        return BuildPlan(allFiles, dependencies);
    }

    private static DeploymentPlan BuildPlan(IReadOnlyList<string> allFiles, Dictionary<string, HashSet<string>> dependencies)
    {
        foreach (var file in allFiles)
            dependencies.TryAdd(file, []);

        var ordered = TopologicalSort(allFiles, dependencies);

        return new DeploymentPlan
        {
            OrderedFiles = ordered,
            Dependencies = dependencies
                .Where(x => x.Value.Count > 0)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<string>)x.Value.OrderBy(y => y, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<string> TopologicalSort(IReadOnlyList<string> files, Dictionary<string, HashSet<string>> dependencies)
    {
        var result = new List<string>();
        var permanent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var temporary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
            Visit(file, dependencies, result, permanent, temporary);

        return result;
    }

    private static void Visit(
        string file,
        Dictionary<string, HashSet<string>> dependencies,
        List<string> result,
        HashSet<string> permanent,
        HashSet<string> temporary)
    {
        if (permanent.Contains(file))
            return;

        if (!temporary.Add(file))
        {
            // Cycles are possible in real PostgreSQL schemas, especially through views/functions.
            // Keep deterministic output instead of failing the whole export.
            return;
        }

        if (dependencies.TryGetValue(file, out var deps))
        {
            foreach (var dependency in deps.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (dependencies.ContainsKey(dependency))
                    Visit(dependency, dependencies, result, permanent, temporary);
            }
        }

        temporary.Remove(file);
        permanent.Add(file);

        if (!result.Contains(file, StringComparer.OrdinalIgnoreCase))
            result.Add(file);
    }

    private static void AddFolderDependencies(
        IEnumerable<string> files,
        IEnumerable<string> dependencyFiles,
        Dictionary<string, HashSet<string>> dependencies)
    {
        foreach (var file in files)
        {
            foreach (var dependency in dependencyFiles)
                AddDependency(dependencies, file, dependency);
        }
    }

    private static void AddDependency(Dictionary<string, HashSet<string>> dependencies, string? file, string? dependency)
    {
        if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(dependency))
            return;

        if (file.Equals(dependency, StringComparison.OrdinalIgnoreCase))
            return;

        if (!dependencies.TryGetValue(file, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dependencies[file] = set;
        }

        set.Add(dependency);
    }

    private static string? TryGet(Dictionary<string, string> files, string schema, string name)
    {
        files.TryGetValue($"{schema}.{name}", out var file);
        return file;
    }

    private static string? TryGetSchemaFile(Dictionary<string, string> schemaFiles, string schema)
    {
        schemaFiles.TryGetValue(schema, out var file);
        return file;
    }

    private static string GetSchemaNameFromFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name;
    }

    private static string GetSchemaQualifiedNameFromFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var parts = name.Split('.', 2);
        return parts.Length == 2 ? $"{parts[0]}.{parts[1]}" : name;
    }

    private static bool ReferencesTable(string sql, string schema, string table)
    {
        return Regex.IsMatch(
            sql,
            $@"\bREFERENCES\s+(?:{Regex.Escape(schema)}\.)?{Regex.Escape(table)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }


    private static bool ReferencesRoutine(string text, string schema, string name)
    {
        return Regex.IsMatch(
            text,
            $@"\b(?:{Regex.Escape(schema)}\.)?{Regex.Escape(name)}\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ReferencesObject(string text, string schema, string name)
    {
        return ContainsWord(text, $"{schema}.{name}") || ContainsWord(text, name);
    }

    private static bool ContainsWord(string text, string value)
    {
        return Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(value)}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Safe(string value)
    {
        return SqlIdentifier.SafeFileName(value);
    }
}
