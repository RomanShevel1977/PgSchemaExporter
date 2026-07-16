using System.Text;
using System.Text.RegularExpressions;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class PgDumpObjectClassifier
{
    private static readonly Regex CreateSchemaRegex = new(@"^\s*CREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^;\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateExtensionRegex = new(@"^\s*CREATE\s+EXTENSION\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^;\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateTypeRegex = new(@"^\s*CREATE\s+TYPE\s+(?<name>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateSequenceRegex = new(@"^\s*CREATE\s+SEQUENCE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateTableRegex = new(@"^\s*CREATE\s+(?:UNLOGGED\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^\s(]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlterTableConstraintRegex = new(@"^\s*ALTER\s+TABLE\s+(?:ONLY\s+)?(?<table>[^\s]+)\s+ADD\s+CONSTRAINT\s+(?<constraint>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateIndexRegex = new(@"^\s*CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^\s]+)\s+ON\s+(?:ONLY\s+)?(?<table>[^\s(]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateViewRegex = new(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+(?<name>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateMaterializedViewRegex = new(@"^\s*CREATE\s+MATERIALIZED\s+VIEW\s+(?<name>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateFunctionRegex = new(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\s+(?<name>[^\s(]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateTriggerRegex = new(@"^\s*CREATE\s+TRIGGER\s+(?<name>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreatePolicyRegex = new(@"^\s*CREATE\s+POLICY\s+(?<name>[^\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"^\s*COMMENT\s+ON\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GrantRegex = new(@"^\s*GRANT\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SqlDumpObject Classify(string statement, int order)
    {
        var normalized = RemovePgDumpNoise(statement).Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return Other(order, statement);

        if (TryMatch(normalized, CreateExtensionRegex, "name", out var extensionName))
            return Build(SqlObjectType.Extension, extensionName, normalized, order);

        if (TryMatch(normalized, CreateSchemaRegex, "name", out var schemaName))
            return new SqlDumpObject { Type = SqlObjectType.Schema, Schema = CleanIdentifier(schemaName), Name = CleanIdentifier(schemaName), Statement = normalized, Order = order };

        if (TryMatch(normalized, CreateTypeRegex, "name", out var typeName))
            return Build(SqlObjectType.Type, typeName, normalized, order);

        if (TryMatch(normalized, CreateSequenceRegex, "name", out var sequenceName))
            return Build(SqlObjectType.Sequence, sequenceName, normalized, order);

        if (TryMatch(normalized, CreateTableRegex, "name", out var tableName))
            return Build(SqlObjectType.Table, tableName, normalized, order);

        var constraintMatch = AlterTableConstraintRegex.Match(normalized);
        if (constraintMatch.Success)
        {
            var (schema, parent) = SplitQualifiedName(constraintMatch.Groups["table"].Value);
            return new SqlDumpObject
            {
                Type = SqlObjectType.Constraint,
                Schema = schema,
                ParentName = parent,
                Name = CleanIdentifier(constraintMatch.Groups["constraint"].Value),
                Statement = normalized,
                Order = order
            };
        }

        var indexMatch = CreateIndexRegex.Match(normalized);
        if (indexMatch.Success)
        {
            var (schema, parent) = SplitQualifiedName(indexMatch.Groups["table"].Value);
            return new SqlDumpObject
            {
                Type = SqlObjectType.Index,
                Schema = schema,
                ParentName = parent,
                Name = CleanIdentifier(indexMatch.Groups["name"].Value),
                Statement = normalized,
                Order = order
            };
        }

        if (TryMatch(normalized, CreateMaterializedViewRegex, "name", out var matViewName))
            return Build(SqlObjectType.View, matViewName, normalized, order);

        if (TryMatch(normalized, CreateViewRegex, "name", out var viewName))
            return Build(SqlObjectType.View, viewName, normalized, order);

        if (TryMatch(normalized, CreateFunctionRegex, "name", out var functionName))
            return Build(SqlObjectType.Function, functionName, normalized, order);

        if (TryMatch(normalized, CreateTriggerRegex, "name", out var triggerName))
            return new SqlDumpObject { Type = SqlObjectType.Trigger, Schema = "triggers", Name = CleanIdentifier(triggerName), Statement = normalized, Order = order };

        if (TryMatch(normalized, CreatePolicyRegex, "name", out var policyName))
            return new SqlDumpObject { Type = SqlObjectType.Policy, Schema = "policies", Name = CleanIdentifier(policyName), Statement = normalized, Order = order };

        if (CommentRegex.IsMatch(normalized))
            return new SqlDumpObject { Type = SqlObjectType.Comment, Schema = "comments", Name = $"comment_{order:D5}", Statement = normalized, Order = order };

        if (GrantRegex.IsMatch(normalized))
            return new SqlDumpObject { Type = SqlObjectType.Grant, Schema = "grants", Name = $"grant_{order:D5}", Statement = normalized, Order = order };

        return Other(order, normalized);
    }

    private static SqlDumpObject Other(int order, string statement)
    {
        return new SqlDumpObject { Type = SqlObjectType.Other, Schema = "misc", Name = $"statement_{order:D5}", Statement = statement, Order = order };
    }

    private static SqlDumpObject Build(SqlObjectType type, string qualifiedName, string statement, int order)
    {
        var (schema, name) = SplitQualifiedName(qualifiedName);
        return new SqlDumpObject { Type = type, Schema = schema, Name = name, Statement = statement, Order = order };
    }

    private static bool TryMatch(string statement, Regex regex, string groupName, out string value)
    {
        var match = regex.Match(statement);
        if (match.Success)
        {
            value = match.Groups[groupName].Value;
            return true;
        }

        value = "";
        return false;
    }

    private static string RemovePgDumpNoise(string statement)
    {
        var result = new StringBuilder(statement.Length);
        var i = 0;
        var len = statement.Length;

        while (i < len)
        {
            var lineStart = i;
            while (i < len && statement[i] != '\n')
                i++;

            var lineEnd = i;
            var contentStart = lineStart;
            while (contentStart < lineEnd && char.IsWhiteSpace(statement[contentStart]))
                contentStart++;

            var isNoise = false;
            if (contentStart < lineEnd)
            {
                if (statement[contentStart] == '-' && contentStart + 1 < lineEnd && statement[contentStart + 1] == '-')
                    isNoise = true;
                else if (IsPrefixIgnoreCase(statement, contentStart, lineEnd, "SET "))
                    isNoise = true;
                else if (IsPrefixIgnoreCase(statement, contentStart, lineEnd, "SELECT pg_catalog.set_config"))
                    isNoise = true;
            }

            if (!isNoise)
            {
                if (result.Length > 0)
                    result.AppendLine();

                result.Append(statement, lineStart, lineEnd - lineStart);
            }

            if (i < len)
                i++;
        }

        var output = result.ToString();
        var trimmed = output.AsSpan().Trim();
        return trimmed.Length == output.Length ? output : trimmed.ToString();
    }

    private static bool IsPrefixIgnoreCase(string text, int start, int end, string prefix)
    {
        if (start + prefix.Length > end)
            return false;

        return text.AsSpan(start, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Schema, string Name) SplitQualifiedName(string qualifiedName)
    {
        var parts = SplitByDotRespectingQuotes(qualifiedName.AsSpan().Trim());

        if (parts.Count >= 2)
            return (CleanIdentifier(parts[^2]), CleanIdentifier(parts[^1]));

        return ("public", CleanIdentifier(qualifiedName));
    }

    private static List<string> SplitByDotRespectingQuotes(ReadOnlySpan<char> value)
    {
        var result = new List<string>();
        var start = 0;
        var inQuotes = false;

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
                inQuotes = !inQuotes;

            if (value[i] == '.' && !inQuotes)
            {
                result.Add(value.Slice(start, i - start).ToString());
                start = i + 1;
            }
        }

        if (start < value.Length)
            result.Add(value.Slice(start, value.Length - start).ToString());

        return result;
    }

    private static string CleanIdentifier(string value)
    {
        var span = value.AsSpan().Trim().TrimEnd(';');

        if (span.Length >= 2 && span[0] == (char)34 && span[span.Length - 1] == (char)34)
        {
            span = span.Slice(1, span.Length - 2);
            if (ContainsConsecutiveQuotes(span))
            {
                var sb = new StringBuilder(span.Length);
                for (var i = 0; i < span.Length; i++)
                {
                    if (i + 1 < span.Length && span[i] == (char)34 && span[i + 1] == (char)34)
                    {
                        sb.Append((char)34);
                        i++;
                    }
                    else
                    {
                        sb.Append(span[i]);
                    }
                }
                return sb.ToString();
            }
            return span.ToString();
        }

        return span.ToString();
    }

    private static bool ContainsConsecutiveQuotes(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length - 1; i++)
        {
            if (span[i] == (char)34 && span[i + 1] == (char)34)
                return true;
        }
        return false;
    }
}
