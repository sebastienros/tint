using System.Text;

namespace Tint.Internal.Regex;

// The textual transformations vscode-textmate (RegExpSource) and Onigwrap (UnicodeCharEscape) apply to grammar
// patterns before they reach the regex engine.
internal static class PatternRewriter
{
    private const string EndOfLine = "$(?!\\n)(?<!\\n)";

    // \z becomes "the real end of the line" (every line is searched with "\n" appended), \A and \G are detected, then
    // \xHH (2 to 8 hex digits) becomes \x{HH} because Oniguruma reads \xHH as an encoded byte, and \x{7fffffff} is
    // clamped as Onigwrap does for the PHP grammar.
    public static string Rewrite(string pattern, out bool hasAnchorA, out bool hasAnchorG)
    {
        hasAnchorA = false;
        hasAnchorG = false;
        StringBuilder? builder = null;
        var lastPushed = 0;
        for (var pos = 0; pos < pattern.Length; pos++)
        {
            if (pattern[pos] == '\\' && pos + 1 < pattern.Length)
            {
                var next = pattern[pos + 1];
                if (next == 'z')
                {
                    builder ??= new StringBuilder(pattern.Length + 16);
                    builder.Append(pattern, lastPushed, pos - lastPushed).Append(EndOfLine);
                    lastPushed = pos + 2;
                }
                else if (next == 'A')
                {
                    hasAnchorA = true;
                }
                else if (next == 'G')
                {
                    hasAnchorG = true;
                }

                pos++;
            }
        }

        var text = pattern;
        if (builder is not null)
        {
            builder.Append(pattern, lastPushed, pattern.Length - lastPushed);
            text = builder.ToString();
        }

        return AddBracesToHexEscapes(text);
    }

    // Onigwrap's UnicodeCharEscape: the regex \\x[A-Fa-f0-9]{2,8}, matched left to right whatever precedes the
    // backslash, becomes \x{...}.
    public static string AddBracesToHexEscapes(string pattern)
    {
        var index = pattern.IndexOf("\\x", StringComparison.Ordinal);
        if (index < 0)
        {
            return pattern;
        }

        StringBuilder? builder = null;
        var lastPushed = 0;
        var i = index;
        while (i < pattern.Length - 1)
        {
            if (pattern[i] == '\\' && pattern[i + 1] == 'x')
            {
                var digits = 0;
                while (digits < 8 && i + 2 + digits < pattern.Length && char.IsAsciiHexDigit(pattern[i + 2 + digits]))
                {
                    digits++;
                }

                if (digits >= 2)
                {
                    builder ??= new StringBuilder(pattern.Length + 8);
                    builder.Append(pattern, lastPushed, i - lastPushed).Append("\\x{").Append(pattern, i + 2, digits).Append('}');
                    i += 2 + digits;
                    lastPushed = i;
                    continue;
                }
            }

            i++;
        }

        if (builder is null)
        {
            return pattern;
        }

        builder.Append(pattern, lastPushed, pattern.Length - lastPushed);
        return builder.Replace("\\x{7fffffff}", "\\x{7ffff}").ToString();
    }

    // vscode-textmate's HAS_BACK_REFERENCES: /\\(\d+)/.
    public static bool HasBackReferences(string text)
    {
        for (var i = text.IndexOf('\\'); i >= 0 && i < text.Length - 1; i = text.IndexOf('\\', i + 1))
        {
            if (char.IsAsciiDigit(text[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    // RegExpSource._buildAnchorCache: a disallowed \A or \G becomes \ followed by U+FFFF.
    public static string ResolveAnchors(string text, bool allowA, bool allowG)
    {
        return string.Create(text.Length, (text, allowA, allowG), static (span, state) =>
        {
            var (source, a, g) = state;
            source.AsSpan().CopyTo(span);
            for (var pos = 0; pos < span.Length - 1; pos++)
            {
                if (span[pos] == '\\')
                {
                    var next = span[pos + 1];
                    if ((next == 'A' && !a) || (next == 'G' && !g))
                    {
                        span[pos + 1] = '￿';
                    }

                    pos++;
                }
            }
        });
    }

    // RegExpSource.resolveBackReferences: every \N becomes the escaped text of capture N of the begin match ('' when
    // the capture does not exist). A lone surrogate in the captured text becomes (?:\p{Cs}|\x{FFFD}), since the
    // engine cannot parse it as a literal.
    public static void ResolveBackReferences(string source, ReadOnlySpan<char> line, ReadOnlySpan<CaptureSpan> captures, CharBuffer builder)
    {
        builder.Clear();
        var lastPushed = 0;
        var i = 0;
        while (i < source.Length - 1)
        {
            if (source[i] == '\\' && char.IsAsciiDigit(source[i + 1]))
            {
                var end = i + 1;
                var number = 0;
                var overflow = false;
                while (end < source.Length && char.IsAsciiDigit(source[end]))
                {
                    if (number > (int.MaxValue - 9) / 10)
                    {
                        overflow = true;
                    }
                    else
                    {
                        number = (number * 10) + (source[end] - '0');
                    }

                    end++;
                }

                builder.Append(source.AsSpan(lastPushed, i - lastPushed));
                if (!overflow && number < captures.Length)
                {
                    var capture = captures[number];
                    AppendEscaped(builder, line.Slice(capture.Start, capture.Length));
                }

                i = end;
                lastPushed = end;
                continue;
            }

            i++;
        }

        builder.Append(source.AsSpan(lastPushed));
    }

    // escapeRegExpCharacters: /[\\|([{}\]).?*+^$]/g gets a backslash.
    public static void AppendEscaped(CharBuffer builder, ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\' or '|' or '(' or '[' or '{' or '}' or ']' or ')' or '.' or '?' or '*' or '+' or '^' or '$':
                    builder.Append('\\');
                    builder.Append(c);
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        builder.Append(c);
                        builder.Append(value[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        builder.Append("(?:\\p{Cs}|\\x{FFFD})");
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }
    }
}
