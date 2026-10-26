using System.Text;

namespace Tint.Differential;

/// <summary>How two engines' outputs for a line differ.</summary>
public enum Difference
{
    /// <summary>Same tokens and styles.</summary>
    None,

    /// <summary>One engine threw, or both with different exception types.</summary>
    Exception,

    /// <summary>One engine ran out of the time limit and the other did not.</summary>
    StoppedEarly,

    /// <summary>Different token offsets or scopes (<c>TokenizeLine</c>).</summary>
    Tokens,

    /// <summary>Same scopes, different colors or font styles (<c>TokenizeLine2</c>).</summary>
    Styles,
}

/// <summary>Normalization and comparison of engine outputs.</summary>
public static class Comparison
{
    /// <summary>
    /// Clamps token ends to the line (vscode-textmate's last token may cover the <c>\n</c> it appends; Tint's stops at
    /// the end of the line) and drops the empty tokens that leaves, except a single one on an empty line.
    /// </summary>
    public static ScopeToken[] NormalizeTokens(ScopeToken[] tokens, int lineLength)
    {
        var result = new List<ScopeToken>(tokens.Length);
        foreach (var token in tokens)
        {
            var clamped = token with { End = Math.Min(token.End, lineLength) };
            if (clamped.End > clamped.Start || (lineLength == 0 && result.Count == 0))
            {
                result.Add(clamped);
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Drops binary tokens starting at or past the end of a non-empty line and merges neighbours with the same style:
    /// only colors and font style are compared, since language ids, token types and bracket flags depend on registry
    /// order and on package.json metadata TextMateSharp does not read.
    /// </summary>
    public static StyleToken[] NormalizeStyles(StyleToken[] styles, int lineLength)
    {
        var result = new List<StyleToken>(styles.Length);
        foreach (var style in styles)
        {
            var normalized = style with { Foreground = style.Foreground.ToUpperInvariant(), Background = style.Background.ToUpperInvariant() };
            if (lineLength > 0 && normalized.Start >= lineLength)
            {
                continue;
            }

            if (result.Count > 0 && result[^1] with { Start = 0 } == normalized with { Start = 0 })
            {
                continue;
            }

            result.Add(normalized);
        }

        return [.. result];
    }

    /// <summary>The first difference between two outputs of the same line.</summary>
    public static Difference Compare(LineOutput tint, LineOutput reference)
    {
        if (tint.Exception is not null || reference.Exception is not null)
        {
            return Type(tint.Exception) == Type(reference.Exception) ? Difference.None : Difference.Exception;
        }

        if (tint.StoppedEarly != reference.StoppedEarly)
        {
            return Difference.StoppedEarly;
        }

        if (tint.StoppedEarly)
        {
            // Both stopped: where depends on timing.
            return Difference.None;
        }

        if (!tint.Tokens.AsSpan().SequenceEqual(reference.Tokens))
        {
            return Difference.Tokens;
        }

        return tint.Styles.AsSpan().SequenceEqual(reference.Styles) ? Difference.None : Difference.Styles;

        static string? Type(string? exception) => exception?[..exception.IndexOf(':')];
    }

    /// <summary>The tokens one per line, for reports: at most 200 tokens, long texts and scope lists shortened.</summary>
    public static string Format(LineOutput output, string line, Difference difference)
    {
        const int MaxTokens = 200;
        if (output.Exception is not null)
        {
            return output.Exception;
        }

        var builder = new StringBuilder();
        if (output.StoppedEarly)
        {
            builder.AppendLine("(stopped early)");
        }

        if (difference == Difference.Styles)
        {
            foreach (var style in output.Styles.Take(MaxTokens))
            {
                builder.Append(style.Start).Append(' ').Append(style.Foreground).Append(' ').Append(style.Background).Append(" fs=").Append(style.FontStyle).AppendLine();
            }
        }
        else
        {
            foreach (var token in output.Tokens.Take(MaxTokens))
            {
                var start = Math.Min(token.Start, line.Length);
                var text = line.AsSpan(start, Math.Max(0, Math.Min(token.End, line.Length) - start));
                builder.Append(token.Start).Append('-').Append(token.End).Append(' ')
                    .Append(Escape(text.Length > 80 ? text[..80] : text)).Append(text.Length > 80 ? "…" : "").Append(' ')
                    .AppendLine(token.Scopes.Length > 600 ? $"…({token.Scopes.Length} chars) {token.Scopes[^300..]}" : token.Scopes);
            }
        }

        var count = difference == Difference.Styles ? output.Styles.Length : output.Tokens.Length;
        if (count > MaxTokens)
        {
            builder.AppendLine($"… {count - MaxTokens} more tokens");
        }

        return builder.ToString();
    }

    /// <summary>A line made printable: control characters and lone surrogates as <c>\uXXXX</c>, quoted.</summary>
    public static string Escape(ReadOnlySpan<char> text)
    {
        var builder = new StringBuilder(text.Length + 2).Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
            }
            else if (char.IsControl(c) || char.IsSurrogate(c) || c == '"' || c == '\\')
            {
                builder.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\t' => "\\t", _ => $"\\u{(int)c:X4}" });
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.Append('"').ToString();
    }
}
