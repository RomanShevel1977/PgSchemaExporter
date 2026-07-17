using System.Text;
using PgSchemaExporter.Core.Models;

namespace PgSchemaExporter.Core.Scripting;

public sealed class PgDumpObjectClassifier
{
    public SqlDumpObject Classify(string statement, int order)
    {
        var normalized = RemovePgDumpNoise(statement).Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return Other(order, statement);

        var firstWord = SqlTokenizer.ReadIdentifier(normalized, 0, out var pos, unquote: false);
        if (firstWord is null)
            return Other(order, normalized);

        if (firstWord.Equals("CREATE", StringComparison.OrdinalIgnoreCase))
            return ClassifyCreate(normalized, pos, order);

        if (firstWord.Equals("ALTER", StringComparison.OrdinalIgnoreCase))
            return ClassifyAlter(normalized, pos, order);

        if (firstWord.Equals("COMMENT", StringComparison.OrdinalIgnoreCase))
            return new SqlDumpObject { Type = SqlObjectType.Comment, Schema = "comments", Name = $"comment_{order:D5}", Statement = normalized, Order = order };

        if (firstWord.Equals("GRANT", StringComparison.OrdinalIgnoreCase))
            return new SqlDumpObject { Type = SqlObjectType.Grant, Schema = "grants", Name = $"grant_{order:D5}", Statement = normalized, Order = order };

        return Other(order, normalized);
    }

    private static SqlDumpObject ClassifyCreate(string sql, int pos, int order)
    {
        pos = SkipCreateModifiers(sql, pos);

        var objectType = SqlTokenizer.ReadIdentifier(sql, pos, out var nextPos, unquote: false);
        if (objectType is null)
            return Other(order, sql);

        if (objectType.Equals("SCHEMA", StringComparison.OrdinalIgnoreCase))
        {
            var name = ReadName(sql, nextPos);
            var clean = name is null ? "" : CleanIdentifier(name);
            return new SqlDumpObject { Type = SqlObjectType.Schema, Schema = clean, Name = clean, Statement = sql, Order = order };
        }

        if (objectType.Equals("EXTENSION", StringComparison.OrdinalIgnoreCase))
            return Build(SqlObjectType.Extension, ReadName(sql, nextPos) ?? "", sql, order);

        if (objectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            return Build(SqlObjectType.Type, ReadName(sql, nextPos) ?? "", sql, order);

        if (objectType.Equals("SEQUENCE", StringComparison.OrdinalIgnoreCase))
            return Build(SqlObjectType.Sequence, ReadName(sql, nextPos) ?? "", sql, order);

        if (objectType.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
        {
            var name = ReadName(sql, nextPos);
            if (name is not null)
                return Build(SqlObjectType.Table, name, sql, order);
        }

        if (objectType.Equals("INDEX", StringComparison.OrdinalIgnoreCase))
            return ClassifyIndex(sql, nextPos, order);

        if (objectType.Equals("VIEW", StringComparison.OrdinalIgnoreCase))
            return Build(SqlObjectType.View, ReadName(sql, nextPos) ?? "", sql, order);

        if (objectType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
            return Build(SqlObjectType.Function, ReadName(sql, nextPos) ?? "", sql, order);

        if (objectType.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase))
            return new SqlDumpObject { Type = SqlObjectType.Trigger, Schema = "triggers", Name = CleanIdentifier(ReadName(sql, nextPos) ?? ""), Statement = sql, Order = order };

        if (objectType.Equals("POLICY", StringComparison.OrdinalIgnoreCase))
            return new SqlDumpObject { Type = SqlObjectType.Policy, Schema = "policies", Name = CleanIdentifier(ReadName(sql, nextPos) ?? ""), Statement = sql, Order = order };

        return Other(order, sql);
    }

    private static SqlDumpObject ClassifyAlter(string sql, int pos, int order)
    {
        var objectType = SqlTokenizer.ReadIdentifier(sql, pos, out var nextPos, unquote: false);
        if (objectType is null || !objectType.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
            return Other(order, sql);

        var tableName = ReadName(sql, nextPos);
        if (tableName is null)
            return Other(order, sql);

        var addConstraintIndex = SqlTokenizer.IndexOfWord(sql, "ADD CONSTRAINT", pos, out var addConstraintLength);
        if (addConstraintIndex < 0)
            return Other(order, sql);

        var afterConstraint = addConstraintIndex + addConstraintLength;
        var remaining = sql[afterConstraint..].TrimStart();
        var constraint = SqlTokenizer.ReadIdentifier(remaining, 0, out _);
        if (constraint is null)
            return Other(order, sql);

        var (schema, parent) = SplitQualifiedName(tableName);
        return new SqlDumpObject
        {
            Type = SqlObjectType.Constraint,
            Schema = schema,
            ParentName = parent,
            Name = CleanIdentifier(constraint),
            Statement = sql,
            Order = order
        };
    }

    private static SqlDumpObject ClassifyIndex(string sql, int pos, int order)
    {
        var remaining = sql[pos..].TrimStart();

        if (SqlTokenizer.StartsWithWord(remaining, "CONCURRENTLY"))
            remaining = remaining["CONCURRENTLY".Length..].TrimStart();

        if (SqlTokenizer.StartsWithWord(remaining, "IF NOT EXISTS"))
            remaining = remaining["IF NOT EXISTS".Length..].TrimStart();

        var name = SqlTokenizer.ReadIdentifier(remaining, 0, out _);
        if (name is null)
            return Other(order, sql);

        var onIndex = SqlTokenizer.IndexOfWord(sql, "ON");
        if (onIndex >= 0)
        {
            var afterOn = onIndex + "ON".Length;
            var onRemaining = SqlTokenizer.SkipLeadingNoise(sql[afterOn..]);
            var table = SqlTokenizer.ReadIdentifier(onRemaining, 0, out _);
            if (table is not null)
            {
                var (schema, parent) = SplitQualifiedName(table);
                var (_, indexName) = SplitQualifiedName(name);
                return new SqlDumpObject
                {
                    Type = SqlObjectType.Index,
                    Schema = schema,
                    ParentName = parent,
                    Name = indexName,
                    Statement = sql,
                    Order = order
                };
            }
        }

        var (_, nameOnly) = SplitQualifiedName(name);
        return new SqlDumpObject { Type = SqlObjectType.Index, Schema = "public", Name = nameOnly, Statement = sql, Order = order };
    }

    private static int SkipCreateModifiers(string sql, int pos)
    {
        while (true)
        {
            var word = SqlTokenizer.ReadIdentifier(sql, pos, out var nextPos, unquote: false);
            if (word is null)
                return pos;

            if (word.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                var nextWord = SqlTokenizer.ReadIdentifier(sql, nextPos, out var afterNext, unquote: false);
                if (nextWord is not null && nextWord.Equals("REPLACE", StringComparison.OrdinalIgnoreCase))
                {
                    pos = afterNext;
                    continue;
                }
            }
            else if (word.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                     word.Equals("UNLOGGED", StringComparison.OrdinalIgnoreCase) ||
                     word.Equals("FOREIGN", StringComparison.OrdinalIgnoreCase) ||
                     word.Equals("MATERIALIZED", StringComparison.OrdinalIgnoreCase))
            {
                pos = nextPos;
                continue;
            }

            return pos;
        }
    }

    private static string? ReadName(string sql, int pos)
    {
        var remaining = SqlTokenizer.SkipLeadingNoise(sql[pos..]);
        return SqlTokenizer.ReadIdentifier(remaining, 0, out _);
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
