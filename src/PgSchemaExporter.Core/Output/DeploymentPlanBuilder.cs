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

        var typeIndex = FileIndex.Build(typeFiles);
        var sequenceIndex = FileIndex.Build(sequenceFiles);
        var tableIndex = FileIndex.Build(tableFiles);
        var viewIndex = FileIndex.Build(viewFiles);
        var functionIndex = FileIndex.BuildFunctions(functionFiles);
        var constraintIndex = FileIndex.BuildConstraints(files.ConstraintFiles);

        foreach (var extension in model.Extensions)
        {
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
            if (tableFile is null)
                continue;

            foreach (var column in table.Columns)
            {
                if (!string.IsNullOrWhiteSpace(column.DataType))
                    ScanForReferences(column.DataType, dependencies, tableFile, typeIndex, FileIndex.Empty, FileIndex.Empty, FileIndex.Empty, functionRequiresCall: false);

                if (!string.IsNullOrWhiteSpace(column.DefaultValue))
                    ScanForReferences(column.DefaultValue!, dependencies, tableFile, FileIndex.Empty, FileIndex.Empty, FileIndex.Empty, sequenceIndex, functionRequiresCall: false);
            }
        }

        foreach (var group in model.Constraints.GroupBy(x => new { x.Schema, x.TableName }))
        {
            var constraintKey = $"{Safe(group.Key.Schema)}.{Safe(group.Key.TableName)}";
            var constraintFile = constraintIndex.Qualified.TryGetValue(constraintKey, out var constraintList) ? constraintList[0] : null;
            var tableFile = TryGet(tableFiles, group.Key.Schema, group.Key.TableName);
            AddDependency(dependencies, constraintFile, tableFile);

            foreach (var constraint in group)
            {
                foreach (var referencedFile in FindReferencedTableFiles(constraint.Definition, tableIndex, constraintIndex))
                    AddDependency(dependencies, constraintFile, referencedFile);
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

            if (!string.IsNullOrEmpty(functionFile) && !string.IsNullOrEmpty(function.Definition))
                ScanForReferences(function.Definition, dependencies, functionFile, typeIndex, tableIndex, viewIndex, functionIndex, functionRequiresCall: true);
        }

        foreach (var view in model.Views)
        {
            var viewFile = TryGet(viewFiles, view.Schema, view.Name);
            AddDependency(dependencies, viewFile, TryGetSchemaFile(schemaFiles, view.Schema));

            if (!string.IsNullOrEmpty(viewFile) && !string.IsNullOrEmpty(view.Definition))
                ScanForReferences(view.Definition, dependencies, viewFile, FileIndex.Empty, tableIndex, viewIndex, functionIndex, functionRequiresCall: false);
        }

        foreach (var trigger in model.Triggers)
        {
            var triggerFile = files.TriggerFiles.FirstOrDefault(x => x.StartsWith($"triggers/{Safe(trigger.Schema)}.{Safe(trigger.TableName)}.{Safe(trigger.Name)}.", StringComparison.OrdinalIgnoreCase));
            AddDependency(dependencies, triggerFile, TryGet(tableFiles, trigger.TableSchema, trigger.TableName));

            if (!string.IsNullOrEmpty(triggerFile) && !string.IsNullOrEmpty(trigger.Definition))
                ScanForReferences(trigger.Definition, dependencies, triggerFile, FileIndex.Empty, FileIndex.Empty, FileIndex.Empty, functionIndex, functionRequiresCall: true);
        }

        foreach (var policy in model.Policies)
        {
            var policyFile = files.PolicyFiles.FirstOrDefault(x => x.StartsWith($"policies/{Safe(policy.Schema)}.{Safe(policy.TableName)}.{Safe(policy.Name)}.", StringComparison.OrdinalIgnoreCase));
            AddDependency(dependencies, policyFile, TryGet(tableFiles, policy.TableSchema, policy.TableName));

            if (!string.IsNullOrEmpty(policyFile) && !string.IsNullOrEmpty(policy.Definition))
                ScanForReferences(policy.Definition, dependencies, policyFile, FileIndex.Empty, FileIndex.Empty, FileIndex.Empty, functionIndex, functionRequiresCall: true);
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

        var sortedDependencies = dependencies.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)x.Value.OrderBy(y => y, StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        var ordered = TopologicalSort(allFiles, sortedDependencies);

        return new DeploymentPlan
        {
            OrderedFiles = ordered,
            Dependencies = sortedDependencies
                .Where(x => x.Value.Count > 0)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.Value,
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<string> TopologicalSort(IReadOnlyList<string> files, Dictionary<string, IReadOnlyList<string>> dependencies)
    {
        var result = new List<string>();
        var resultSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permanent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var temporary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
            Visit(file, dependencies, result, resultSet, permanent, temporary);

        return result;
    }

    private static void Visit(
        string file,
        Dictionary<string, IReadOnlyList<string>> dependencies,
        List<string> result,
        HashSet<string> resultSet,
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
            foreach (var dependency in deps)
            {
                if (dependencies.ContainsKey(dependency))
                    Visit(dependency, dependencies, result, resultSet, permanent, temporary);
            }
        }

        temporary.Remove(file);
        permanent.Add(file);

        if (resultSet.Add(file))
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

    private static string Safe(string value)
    {
        return SqlIdentifier.SafeFileName(value);
    }

    private sealed class FileIndex
    {
        public Dictionary<string, List<string>> Qualified { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<(string Schema, string File)>> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public static FileIndex Empty { get; } = new FileIndex();

        public void Add(string schema, string name, string file)
        {
            Add(Qualified, $"{schema}.{name}", file);
            Add(ByName, name, (schema, file));
        }

        public static FileIndex Build(IReadOnlyDictionary<string, string> files)
        {
            var index = new FileIndex();
            foreach (var (key, file) in files)
            {
                var dot = key.LastIndexOf('.');
                var schema = dot >= 0 ? key[..dot] : "public";
                var name = dot >= 0 ? key[(dot + 1)..] : key;
                index.Add(schema, name, file);
            }
            return index;
        }

        public static FileIndex BuildFunctions(IReadOnlyList<string> functionFiles)
        {
            var index = new FileIndex();
            const string prefix = "functions/";
            foreach (var file in functionFiles)
            {
                if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = file[prefix.Length..];
                var withoutExt = Path.GetFileNameWithoutExtension(relative);
                var parts = withoutExt.Split('.', 3);
                if (parts.Length >= 2)
                    index.Add(parts[0], parts[1], file);
            }
            return index;
        }

        public static FileIndex BuildConstraints(IReadOnlyList<string> constraintFiles)
        {
            var index = new FileIndex();
            const string prefix = "constraints/";
            foreach (var file in constraintFiles)
            {
                if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = file[prefix.Length..];
                var withoutExt = Path.GetFileNameWithoutExtension(relative);
                var parts = withoutExt.Split('.', 3);
                if (parts.Length >= 2)
                    index.Add(parts[0], parts[1], file);
            }
            return index;
        }

        private static void Add(Dictionary<string, List<string>> dict, string key, string file)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<string>();
                dict[key] = list;
            }
            list.Add(file);
        }

        private static void Add(Dictionary<string, List<(string Schema, string File)>> dict, string key, (string Schema, string File) entry)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<(string Schema, string File)>();
                dict[key] = list;
            }
            list.Add(entry);
        }
    }

    private static void ScanForReferences(
        string text,
        Dictionary<string, HashSet<string>> dependencies,
        string fromFile,
        FileIndex typeIndex,
        FileIndex tableIndex,
        FileIndex viewIndex,
        FileIndex functionIndex,
        bool functionRequiresCall)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (!IsIdentifierStart(text[i]))
            {
                i++;
                continue;
            }

            var token = SqlTokenizer.ReadIdentifier(text, i, out var after, unquote: true);
            if (token is null)
            {
                i++;
                continue;
            }

            i = after;

            var j = i;
            while (j < text.Length && char.IsWhiteSpace(text[j]))
                j++;
            var isCall = j < text.Length && text[j] == '(';

            AddMatches(dependencies, fromFile, typeIndex, token);
            AddMatches(dependencies, fromFile, tableIndex, token);
            AddMatches(dependencies, fromFile, viewIndex, token);

            if (!functionRequiresCall || isCall)
                AddMatches(dependencies, fromFile, functionIndex, token);
        }
    }

    private static void AddMatches(Dictionary<string, HashSet<string>> dependencies, string? fromFile, FileIndex index, string token)
    {
        if (string.IsNullOrEmpty(fromFile))
            return;

        if (index.Qualified.TryGetValue(token, out var qualifiedFiles))
        {
            foreach (var file in qualifiedFiles)
                AddDependency(dependencies, fromFile, file);
        }

        var dot = token.LastIndexOf('.');
        var name = dot >= 0 ? token[(dot + 1)..] : token;
        if (index.ByName.TryGetValue(name, out var nameEntries))
        {
            foreach (var (_, file) in nameEntries)
                AddDependency(dependencies, fromFile, file);
        }
    }

    private static IEnumerable<string> FindReferencedTableFiles(string definition, FileIndex tableIndex, FileIndex constraintIndex)
    {
        var i = 0;
        while (i < definition.Length)
        {
            if (!IsIdentifierStart(definition[i]))
            {
                i++;
                continue;
            }

            var token = SqlTokenizer.ReadIdentifier(definition, i, out var after, unquote: true);
            if (token is null)
            {
                i++;
                continue;
            }

            i = after;

            if (!token.Equals("REFERENCES", StringComparison.OrdinalIgnoreCase))
                continue;

            var remaining = definition[i..].TrimStart();
            var tableToken = SqlTokenizer.ReadIdentifier(remaining, 0, out _, unquote: true);
            if (tableToken is null)
                continue;

            if (tableToken.Contains('.'))
            {
                if (tableIndex.Qualified.TryGetValue(tableToken, out var tableFiles))
                {
                    foreach (var tableFile in tableFiles)
                    {
                        var file = TryGetConstraintOrTableFile(tableToken, tableFile, constraintIndex);
                        if (file is not null)
                            yield return file;
                    }
                }
            }
            else
            {
                if (tableIndex.ByName.TryGetValue(tableToken, out var entries))
                {
                    foreach (var (schema, tableFile) in entries)
                    {
                        var key = $"{schema}.{tableToken}";
                        var file = TryGetConstraintOrTableFile(key, tableFile, constraintIndex);
                        if (file is not null)
                            yield return file;
                    }
                }
            }
        }
    }

    private static string? TryGetConstraintOrTableFile(string qualifiedKey, string tableFile, FileIndex constraintIndex)
    {
        if (constraintIndex.Qualified.TryGetValue(qualifiedKey, out var list) && list.Count > 0)
            return list[0];
        return tableFile;
    }

    private static bool IsIdentifierStart(char c)
        => char.IsLetter(c) || c == '_' || c == '$' || c == '"';
}
