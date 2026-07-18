using System.Text;

namespace PgSchemaExporter.Core.Scripting;

/// <summary>
/// Centralized, allocation-conscious SQL tokenizer.  It is responsible for the
/// single-pass scanning that used to live inside <see cref="SqlStatementSplitter"/>
/// and can be reused by other SQL parsers to avoid duplicated parsing passes.
/// </summary>
public static class SqlTokenizer
{
    /// <summary>
    /// Splits a SQL batch into individual statements respecting single/double
    /// quotes, dollar-quoted blocks, and line/block comments.
    /// </summary>
    public static IReadOnlyList<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder(Math.Min(sql.Length, 4096));

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarTag = null;

        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                current.Append(ch);
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                current.Append(ch);
                if (ch == '*' && next == '/')
                {
                    current.Append(next);
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (dollarTag is not null)
            {
                if (ch == '$' && IsAt(sql, i, dollarTag))
                {
                    current.Append(dollarTag);
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (inSingleQuote)
            {
                current.Append(ch);

                if (ch == '\'' && next == '\'')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                current.Append(ch);

                if (ch == '"' && next == '"')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '-' && next == '-')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inLineComment = true;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inBlockComment = true;
                continue;
            }

            if (ch == '\'')
            {
                current.Append(ch);
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                current.Append(ch);
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$')
            {
                var tag = TryReadDollarTag(sql, i);
                if (tag is not null)
                {
                    current.Append(tag);
                    i += tag.Length - 1;
                    dollarTag = tag;
                    continue;
                }
            }

            if (ch == ';')
            {
                current.Append(ch);

                var raw = current.ToString();
                var statement = raw.AsSpan().Trim();
                if (!statement.IsEmpty)
                    statements.Add(statement.Length == raw.Length ? raw : statement.ToString());

                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tailRaw = current.ToString();
        var tail = tailRaw.AsSpan().Trim();
        if (!tail.IsEmpty)
            statements.Add(tail.Length == tailRaw.Length ? tailRaw : tail.ToString());

        return statements;
    }

    /// <summary>
    /// Collapses all whitespace runs to a single space and trims the statement.
    /// Produces a canonical form that can be used for ordinal comparison while
    /// preserving lexical identity.
    /// </summary>
    public static string NormalizeStatement(string sql)
    {
        var span = sql.AsSpan().Trim();
        if (span.IsEmpty)
            return "";

        var sb = new StringBuilder(span.Length);
        var lastWasSpace = false;

        foreach (var c in span)
        {
            if (char.IsWhiteSpace(c))
            {
                lastWasSpace = true;
                continue;
            }

            if (lastWasSpace && sb.Length > 0)
                sb.Append(' ');

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds the matching closing parenthesis for the opening parenthesis at
    /// <paramref name="openIndex"/>, ignoring parentheses inside quoted literals,
    /// comments and dollar-quoted blocks.
    /// </summary>
    public static int FindMatchingParen(string text, int openIndex)
    {
        if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != '(')
            return -1;

        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarTag = null;

        for (var i = openIndex; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (dollarTag is not null)
            {
                if (ch == '$' && IsAt(text, i, dollarTag))
                {
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                }
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'' && next == '\'')
                {
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '"' && next == '"')
                {
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '-' && next == '-')
            {
                i++;
                inLineComment = true;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                i++;
                inBlockComment = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$')
            {
                var tag = TryReadDollarTag(text, i);
                if (tag is not null)
                {
                    i += tag.Length - 1;
                    dollarTag = tag;
                    continue;
                }
            }

            if (ch == '(')
                depth++;
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits <paramref name="text"/> by <paramref name="delimiter"/> only at the
    /// top level, outside quoted literals, comments and dollar-quoted blocks.
    /// Useful for column lists, function arguments and comma-separated clauses.
    /// </summary>
    public static IReadOnlyList<string> SplitTopLevel(string text, char delimiter = ',')
    {
        var parts = new List<string>();
        var current = new StringBuilder(text.Length);

        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarTag = null;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                current.Append(ch);
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                current.Append(ch);
                if (ch == '*' && next == '/')
                {
                    current.Append(next);
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (dollarTag is not null)
            {
                if (ch == '$' && IsAt(text, i, dollarTag))
                {
                    current.Append(dollarTag);
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (inSingleQuote)
            {
                current.Append(ch);

                if (ch == '\'' && next == '\'')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;

                continue;
            }

            if (inDoubleQuote)
            {
                current.Append(ch);

                if (ch == '"' && next == '"')
                {
                    current.Append(next);
                    i++;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (ch == '-' && next == '-')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inLineComment = true;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                inBlockComment = true;
                continue;
            }

            if (ch == '\'')
            {
                current.Append(ch);
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                current.Append(ch);
                inDoubleQuote = true;
                continue;
            }

            if (ch == '$')
            {
                var tag = TryReadDollarTag(text, i);
                if (tag is not null)
                {
                    current.Append(tag);
                    i += tag.Length - 1;
                    dollarTag = tag;
                    continue;
                }
            }

            if (ch == '(')
            {
                depth++;
                current.Append(ch);
            }
            else if (ch == ')')
            {
                depth--;
                current.Append(ch);
            }
            else if (ch == delimiter && depth == 0)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString().Trim());

        return parts;
    }

    private static string? TryReadDollarTag(string sql, int start)
    {
        var end = start + 1;

        while (end < sql.Length)
        {
            var ch = sql[end];

            if (ch == '$')
                return sql[start..(end + 1)];

            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return null;

            end++;
        }

        return null;
    }

    private static bool IsAt(string sql, int index, string value)
    {
        if (index + value.Length > sql.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (sql[index + i] != value[i])
                return false;
        }

        return true;
    }

    public static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    public static int IndexOfWord(string text, string word, int start = 0)
        => IndexOfWord(text, word, start, out _);

    public static int IndexOfWord(string text, string word, int start, out int matchedLength)
    {
        matchedLength = 0;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word) || start < 0 || start > text.Length)
            return -1;

        if (word.IndexOf(' ') < 0)
            return IndexOfSingleWord(text, word, start, out matchedLength);

        var words = word.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return -1;

        var first = words[0];
        var i = start;

        while (i <= text.Length - first.Length)
        {
            var found = text.IndexOf(first, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (!IsWordBoundaryBefore(text, found))
            {
                i = found + 1;
                continue;
            }

            var pos = found + first.Length;
            var matched = true;

            for (var k = 1; k < words.Length; k++)
            {
                var sawWhitespace = false;
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                {
                    sawWhitespace = true;
                    pos++;
                }

                if (!sawWhitespace)
                {
                    matched = false;
                    break;
                }

                var w = words[k];
                if (pos + w.Length > text.Length || !text.AsSpan(pos, w.Length).Equals(w, StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }

                pos += w.Length;
            }

            if (matched)
            {
                if (pos >= text.Length || !IsWordChar(text[pos]))
                {
                    matchedLength = pos - found;
                    return found;
                }
            }

            i = found + 1;
        }

        return -1;
    }

    public static int IndexAfterWord(string text, string word, int start = 0)
    {
        var index = IndexOfWord(text, word, start, out var length);
        return index < 0 ? -1 : index + length;
    }

    public static int LastIndexOfWord(string text, string word, int start = -1)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
            return -1;

        if (word.IndexOf(' ') < 0)
            return LastIndexOfSingleWord(text, word, start);

        var words = word.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return -1;

        var first = words[0];
        var searchEnd = text.Length - first.Length;
        var i = start >= 0 && start <= searchEnd ? start : searchEnd;

        while (i >= 0)
        {
            var found = text.LastIndexOf(first, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (MatchesWordAt(text, found, word))
                return found;

            i = found - 1;
        }

        return -1;
    }

    public static bool ContainsWord(string text, string word)
        => IndexOfWord(text, word) >= 0;

    public static bool StartsWithWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
            return false;
        return IndexOfWord(text, word, 0) == 0;
    }

    public static bool MatchesWordAt(string text, int index, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word) || index < 0 || index >= text.Length)
            return false;

        if (word.IndexOf(' ') < 0)
            return MatchesSingleWordAt(text, index, word);

        var words = word.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        if (index + words[0].Length > text.Length)
            return false;

        if (!IsWordBoundaryBefore(text, index))
            return false;

        var pos = index;
        for (var k = 0; k < words.Length; k++)
        {
            if (k > 0)
            {
                var sawWhitespace = false;
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                {
                    sawWhitespace = true;
                    pos++;
                }

                if (!sawWhitespace)
                    return false;
            }

            var w = words[k];
            if (pos + w.Length > text.Length || !text.AsSpan(pos, w.Length).Equals(w, StringComparison.OrdinalIgnoreCase))
                return false;

            pos += w.Length;
        }

        if (pos < text.Length && IsWordChar(text[pos]))
            return false;

        return true;
    }

    public static string SkipLeadingNoise(string text)
    {
        text = text.TrimStart();

        while (true)
        {
            if (StartsWithWord(text, "IF NOT EXISTS"))
                text = text["IF NOT EXISTS".Length..].TrimStart();
            else if (StartsWithWord(text, "ONLY"))
                text = text["ONLY".Length..].TrimStart();
            else
                break;
        }

        return text;
    }

    public static string? ReadIdentifier(string text)
        => ReadIdentifier(text, 0, out _);

    public static string? ReadIdentifier(string text, int start, out int after)
        => ReadIdentifier(text, start, out after, unquote: false);

    public static string? ReadIdentifier(string text, int start, out int after, bool unquote)
    {
        after = start;
        if (string.IsNullOrEmpty(text))
            return null;

        var i = start;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        if (i >= text.Length)
            return null;

        // Fast path: plain unquoted identifier (possibly schema-qualified).
        if (text[i] != '"')
        {
            var startIdx = i;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.')
                {
                    i++;
                    continue;
                }

                break;
            }

            if (i > startIdx)
            {
                after = i;
                var raw = text[startIdx..i];
                return unquote ? UnquoteQualified(raw) : raw;
            }

            return null;
        }

        var sb = new StringBuilder();
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"')
            {
                // Capture the quoted identifier, including surrounding quotes and escaped "".
                sb.Append(c);
                i++;

                while (i < text.Length)
                {
                    c = text[i];
                    sb.Append(c);

                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    i++;
                }

                // Continue if the quoted identifier is schema-qualified.
                if (i < text.Length && text[i] == '.')
                {
                    sb.Append('.');
                    i++;
                    continue;
                }

                after = i;
                if (!unquote)
                    return sb.ToString();

                return UnquoteQualified(sb.ToString());
            }

            if (char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.')
            {
                sb.Append(c);
                i++;
                continue;
            }

            break;
        }

        after = i;
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string UnquoteQualified(string raw)
    {
        if (raw.IndexOf('"') < 0)
            return raw;

        var parts = SplitTopLevel(raw, '.');
        var result = new StringBuilder(raw.Length);
        var first = true;

        foreach (var part in parts)
        {
            if (!first)
                result.Append('.');

            first = false;
            var p = part.Trim();
            if (p.Length >= 2 && p[0] == '"' && p[^1] == '"')
                result.Append(p[1..^1].Replace("\"\"", "\""));
            else
                result.Append(p);
        }

        return result.ToString();
    }

    private static bool IsWordBoundaryBefore(string text, int index)
    {
        if (index == 0)
            return true;

        return !IsWordChar(text[index - 1]);
    }

    private static int IndexOfSingleWord(string text, string word, int start, out int matchedLength)
    {
        matchedLength = 0;
        var i = start;

        while (i <= text.Length - word.Length)
        {
            var found = text.IndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (!IsWordBoundaryBefore(text, found))
            {
                i = found + 1;
                continue;
            }

            var pos = found + word.Length;
            if (pos >= text.Length || !IsWordChar(text[pos]))
            {
                matchedLength = word.Length;
                return found;
            }

            i = found + 1;
        }

        return -1;
    }

    private static bool MatchesSingleWordAt(string text, int index, string word)
    {
        if (index + word.Length > text.Length)
            return false;

        if (!IsWordBoundaryBefore(text, index))
            return false;

        if (!text.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase))
            return false;

        var pos = index + word.Length;
        if (pos < text.Length && IsWordChar(text[pos]))
            return false;

        return true;
    }

    private static int LastIndexOfSingleWord(string text, string word, int start)
    {
        var searchEnd = text.Length - word.Length;
        var i = start >= 0 && start <= searchEnd ? start : searchEnd;

        while (i >= 0)
        {
            var found = text.LastIndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            if (MatchesSingleWordAt(text, found, word))
                return found;

            i = found - 1;
        }

        return -1;
    }

    /// <summary>
    /// Tokenizes a SQL statement into a sequence of <see cref="SqlToken"/> values.
    /// This is the foundation for unifying parser logic: every parser can consume
    /// the same token stream instead of re-implementing quote/comment/dollar-quoting
    /// awareness.
    /// </summary>
    public static IReadOnlyList<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                var start = i;
                while (i < sql.Length && char.IsWhiteSpace(sql[i]))
                    i++;
                tokens.Add(new SqlToken(SqlTokenKind.Whitespace, sql[start..i], start, i - start));
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var start = i;
                i += 2;
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                tokens.Add(new SqlToken(SqlTokenKind.LineComment, sql[start..i], start, i - start));
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                if (i + 1 < sql.Length)
                    i += 2;
                tokens.Add(new SqlToken(SqlTokenKind.BlockComment, sql[start..i], start, i - start));
                continue;
            }

            if (c == '\'')
            {
                var start = i;
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                tokens.Add(new SqlToken(SqlTokenKind.StringLiteral, sql[start..i], start, i - start));
                continue;
            }

            if (c == '"')
            {
                var start = i;
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '"')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, sql[start..i], start, i - start));
                continue;
            }

            if (c == '$')
            {
                var tag = TryReadDollarTag(sql, i);
                if (tag is not null)
                {
                    var start = i;
                    var close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (close >= 0)
                    {
                        i = close + tag.Length;
                        tokens.Add(new SqlToken(SqlTokenKind.DollarQuoted, sql[start..i], start, i - start));
                        continue;
                    }
                }
            }

            if (IsIdentifierChar(c))
            {
                var start = i;
                while (i < sql.Length && IsIdentifierChar(sql[i]))
                    i++;
                tokens.Add(new SqlToken(SqlTokenKind.Word, sql[start..i], start, i - start));
                continue;
            }

            if (c is '(' or ')' or ',' or ';' or '.')
            {
                tokens.Add(new SqlToken(SqlTokenKind.Symbol, sql[i..(i + 1)], i, 1));
                i++;
                continue;
            }

            tokens.Add(new SqlToken(SqlTokenKind.Other, sql[i..(i + 1)], i, 1));
            i++;
        }

        return tokens;
    }

    /// <summary>
    /// Finds the index of the first token that starts the <paramref name="keyword"/>
    /// sequence. Multi-word keywords (e.g. "FOREIGN KEY") are matched across
    /// whitespace/comments. Returns -1 if not found.
    /// </summary>
    public static int FindKeyword(IReadOnlyList<SqlToken> tokens, string keyword, int startToken = 0)
    {
        var words = keyword.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return -1;

        for (var i = startToken; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != SqlTokenKind.Word)
                continue;
            if (!tokens[i].Text.Equals(words[0], StringComparison.OrdinalIgnoreCase))
                continue;

            var pos = i + 1;
            var matched = true;

            for (var k = 1; k < words.Length; k++)
            {
                // Skip whitespace/comments between words.
                while (pos < tokens.Count && (tokens[pos].Kind == SqlTokenKind.Whitespace || tokens[pos].IsComment))
                    pos++;

                if (pos >= tokens.Count || tokens[pos].Kind != SqlTokenKind.Word || !tokens[pos].Text.Equals(words[k], StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }

                pos++;
            }

            if (matched)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Reads a possibly schema-qualified identifier starting at <paramref name="startTokenIndex"/>,
    /// skipping whitespace and comments. Returns the identifier text (with any quotes preserved)
    /// and the index of the first token after the identifier.
    /// </summary>
    public static string? ReadIdentifier(IReadOnlyList<SqlToken> tokens, int startTokenIndex, out int afterTokenIndex)
    {
        afterTokenIndex = startTokenIndex;
        var pos = SkipTrivia(tokens, startTokenIndex);

        var sb = new StringBuilder();
        var first = true;

        while (pos < tokens.Count)
        {
            var token = tokens[pos];

            if (token.Kind == SqlTokenKind.Word || token.Kind == SqlTokenKind.QuotedIdentifier)
            {
                if (!first)
                    return null; // a second part without a separating dot
                sb.Append(token.Text);
                pos++;
                first = false;
                continue;
            }

            if (token.Kind == SqlTokenKind.Symbol && token.Text == ".")
            {
                if (first || pos + 1 >= tokens.Count)
                    return null;
                var next = tokens[pos + 1];
                if (next.Kind != SqlTokenKind.Word && next.Kind != SqlTokenKind.QuotedIdentifier)
                    return null;
                sb.Append('.');
                sb.Append(next.Text);
                pos += 2;
                continue;
            }

            break;
        }

        if (sb.Length == 0)
            return null;

        afterTokenIndex = pos;
        return sb.ToString();
    }

    /// <summary>
    /// Returns the text position immediately following <paramref name="keyword"/>
    /// and any leading "IF NOT EXISTS" / "ONLY" noise. Returns -1 if the keyword
    /// is not found or no identifier follows it.
    /// </summary>
    public static int ReadNameAfter(IReadOnlyList<SqlToken> tokens, string keyword, out string? name)
    {
        name = null;
        var keywordIndex = FindKeyword(tokens, keyword);
        if (keywordIndex < 0)
            return -1;

        var pos = keywordIndex + 1;
        pos = SkipLeadingNoise(tokens, pos);

        var identifier = ReadIdentifier(tokens, pos, out var afterIdentifier);
        if (identifier is null)
            return -1;

        name = identifier;
        return tokens[afterIdentifier - 1].Start + tokens[afterIdentifier - 1].Length;
    }

    /// <summary>
    /// Finds the start character index of the word <paramref name="keyword"/>
    /// in <paramref name="sql"/>, returning -1 if not found.
    /// </summary>
    public static int FindKeyword(string sql, string keyword)
    {
        var tokens = Tokenize(sql);
        var index = FindKeyword(tokens, keyword);
        return index < 0 ? -1 : tokens[index].Start;
    }

    /// <summary>
    /// Reads a possibly schema-qualified identifier that follows <paramref name="keyword"/>
    /// in <paramref name="sql"/>, skipping leading noise such as "IF NOT EXISTS" or "ONLY".
    /// </summary>
    public static string? ReadNameAfter(string sql, string keyword)
    {
        var tokens = Tokenize(sql);
        ReadNameAfter(tokens, keyword, out var name);
        return name;
    }

    /// <summary>
    /// Finds the first '(' token at or after <paramref name="startTokenIndex"/> and
    /// returns the substring between it and its matching ')'.
    /// </summary>
    public static string? ReadParenthesized(IReadOnlyList<SqlToken> tokens, string sql, int startTokenIndex, out int afterTokenIndex)
    {
        afterTokenIndex = startTokenIndex;

        for (var i = startTokenIndex; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == SqlTokenKind.Symbol && tokens[i].Text == "(")
            {
                var open = tokens[i].Start;
                var close = FindMatchingParen(sql, open);
                if (close < 0)
                    return null;

                afterTokenIndex = i + 1;
                while (afterTokenIndex < tokens.Count && (tokens[afterTokenIndex].Kind == SqlTokenKind.Whitespace || tokens[afterTokenIndex].IsComment))
                    afterTokenIndex++;

                return sql[(open + 1)..close];
            }
        }

        return null;
    }

    private static int SkipTrivia(IReadOnlyList<SqlToken> tokens, int start)
    {
        var pos = start;
        while (pos < tokens.Count && (tokens[pos].Kind == SqlTokenKind.Whitespace || tokens[pos].IsComment))
            pos++;
        return pos;
    }

    private static int SkipNoise(IReadOnlyList<SqlToken> tokens, int start)
    {
        var pos = SkipTrivia(tokens, start);
        while (pos < tokens.Count)
        {
            var token = tokens[pos];

            if (token.Kind == SqlTokenKind.Word && token.Text.Equals("ONLY", StringComparison.OrdinalIgnoreCase))
            {
                pos = SkipTrivia(tokens, pos + 1);
                continue;
            }

            // "IF NOT EXISTS"
            if (token.Kind == SqlTokenKind.Word && token.Text.Equals("IF", StringComparison.OrdinalIgnoreCase)
                && NextNonNoise(tokens, pos + 1) is { } notPos
                && tokens[notPos].Kind == SqlTokenKind.Word && tokens[notPos].Text.Equals("NOT", StringComparison.OrdinalIgnoreCase)
                && NextNonNoise(tokens, notPos + 1) is { } existsPos
                && tokens[existsPos].Kind == SqlTokenKind.Word && tokens[existsPos].Text.Equals("EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                pos = SkipTrivia(tokens, existsPos + 1);
                continue;
            }

            break;
        }

        return pos;
    }

    private static int SkipLeadingNoise(IReadOnlyList<SqlToken> tokens, int start)
    {
        var pos = SkipTrivia(tokens, start);
        while (pos < tokens.Count)
        {
            var token = tokens[pos];

            if (token.Kind == SqlTokenKind.Word &&
                (token.Text.Equals("ONLY", StringComparison.OrdinalIgnoreCase) ||
                 token.Text.Equals("CONCURRENTLY", StringComparison.OrdinalIgnoreCase)))
            {
                pos = SkipTrivia(tokens, pos + 1);
                continue;
            }

            // "IF NOT EXISTS"
            if (token.Kind == SqlTokenKind.Word && token.Text.Equals("IF", StringComparison.OrdinalIgnoreCase)
                && NextNonTrivia(tokens, pos + 1) is { } notPos
                && tokens[notPos].Text.Equals("NOT", StringComparison.OrdinalIgnoreCase)
                && NextNonTrivia(tokens, notPos + 1) is { } existsPos
                && tokens[existsPos].Text.Equals("EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                pos = SkipTrivia(tokens, existsPos + 1);
                continue;
            }

            break;
        }

        return pos;
    }

    private static int? NextNonTrivia(IReadOnlyList<SqlToken> tokens, int start)
    {
        var pos = start;
        while (pos < tokens.Count && (tokens[pos].Kind == SqlTokenKind.Whitespace || tokens[pos].IsComment))
            pos++;

        return pos < tokens.Count ? pos : (int?)null;
    }

    private static int? NextNonNoise(IReadOnlyList<SqlToken> tokens, int start)
    {
        var pos = start;
        while (pos < tokens.Count && (tokens[pos].Kind == SqlTokenKind.Whitespace || tokens[pos].IsComment))
            pos++;

        return pos < tokens.Count ? pos : (int?)null;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}

public enum SqlTokenKind
{
    Whitespace,
    LineComment,
    BlockComment,
    StringLiteral,
    QuotedIdentifier,
    DollarQuoted,
    Word,
    Symbol,
    Other
}

public readonly record struct SqlToken(SqlTokenKind Kind, string Text, int Start, int Length)
{
    public bool IsComment => Kind is SqlTokenKind.LineComment or SqlTokenKind.BlockComment;
    public ReadOnlySpan<char> Span => Text.AsSpan();
    public override string ToString() => Text;
}
