using System.Buffers;
using System.Text;

namespace Tint.Rendering;

/// <summary>
/// Renders tokenized code for terminals: 24-bit SGR sequences (<c>ESC[38;2;r;g;bm</c>) for the theme's colors, bold,
/// italic, underline and strikethrough, reset at the end of every line. Immutable and thread-safe; the tokenizer
/// passed to it is not.
/// </summary>
public sealed class AnsiRenderer
{
    private const string Reset = "\u001b[0m";

    private readonly ThemePalette _palette;

    /// <summary>Creates a renderer for a theme.</summary>
    public AnsiRenderer(Theme theme, AnsiRendererOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Options = options ?? AnsiRendererOptions.Default;
        _palette = new ThemePalette(theme);
    }

    /// <summary>The theme.</summary>
    public Theme Theme { get; }

    /// <summary>The rendering settings.</summary>
    public AnsiRendererOptions Options { get; }

    /// <summary>
    /// Renders code to a writer. Lines end at <c>\n</c> or <c>\r\n</c> and are written with <c>\n</c>; the output ends
    /// with a newline when the code does.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="tokenizer">A tokenizer with this renderer's theme. It starts from <see cref="StateStack.Initial"/>.</param>
    /// <param name="writer">Where the text goes.</param>
    /// <exception cref="ArgumentException">The tokenizer uses another theme.</exception>
    public void Render(ReadOnlySpan<char> code, Tokenizer tokenizer, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new TextWriterSink(writer);
        Render(code, tokenizer, ref sink);
    }

    /// <inheritdoc cref="Render(ReadOnlySpan{char}, Tokenizer, TextWriter)"/>
    public void Render(ReadOnlySpan<char> code, Tokenizer tokenizer, IBufferWriter<char> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new BufferWriterSink(writer);
        Render(code, tokenizer, ref sink);
    }

    /// <inheritdoc cref="Render(ReadOnlySpan{char}, Tokenizer, TextWriter)"/>
    public void Render(ReadOnlySpan<char> code, Tokenizer tokenizer, StringBuilder writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new StringBuilderSink(writer);
        Render(code, tokenizer, ref sink);
    }

    /// <summary>Renders code to a string.</summary>
    /// <inheritdoc cref="Render(ReadOnlySpan{char}, Tokenizer, TextWriter)"/>
    public string Render(ReadOnlySpan<char> code, Tokenizer tokenizer)
    {
        var sink = new PooledCharSink((code.Length * 3) + 64);
        try
        {
            Render(code, tokenizer, ref sink);
            return sink.ToString();
        }
        finally
        {
            sink.Dispose();
        }
    }

    internal void Render<TSink>(ReadOnlySpan<char> code, Tokenizer tokenizer, ref TSink sink)
        where TSink : struct, ICharSink
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        if (!ReferenceEquals(tokenizer.Theme, Theme))
        {
            throw new ArgumentException($"The tokenizer uses the theme '{tokenizer.Theme.Name}', not the renderer's theme '{Theme.Name}'.", nameof(tokenizer));
        }

        var state = StateStack.Initial;
        var reader = new LineReader(code);
        var first = true;
        while (reader.TryRead(out var line))
        {
            if (!first)
            {
                sink.Write('\n');
            }

            first = false;
            WriteLine(line, tokenizer.TokenizeLineEncoded(line, state, out state), ref sink);
        }

        if (reader.EndsWithNewline)
        {
            sink.Write('\n');
        }
    }

    private void WriteLine<TSink>(ReadOnlySpan<char> line, ReadOnlySpan<EncodedToken> tokens, ref TSink sink)
        where TSink : struct, ICharSink
    {
        // What the terminal currently has: -1 for the terminal's own color.
        var foreground = -1;
        var background = -1;
        var fontStyle = FontStyle.None;
        var styled = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var start = Math.Min(tokens[i].Start, line.Length);
            var end = i + 1 < tokens.Length ? Math.Min(tokens[i + 1].Start, line.Length) : line.Length;
            if (i == 0)
            {
                // Text before the first token, if any, keeps the terminal's style.
                sink.Write(line[..start]);
            }

            if (end <= start)
            {
                continue;
            }

            var metadata = tokens[i].Metadata;
            var nextForeground = _palette.GetRgb(EncodedTokenAttributes.GetForeground(metadata));
            var nextBackground = Options.Background ? _palette.GetRgb(EncodedTokenAttributes.GetBackground(metadata)) : -1;
            var nextFontStyle = EncodedTokenAttributes.GetFontStyle(metadata);
            nextFontStyle = nextFontStyle == FontStyle.NotSet ? FontStyle.None : nextFontStyle & (FontStyle)0xF;

            // Turning an attribute off has no portable sequence for every terminal: reset and set what is left.
            if ((fontStyle & ~nextFontStyle) != 0 || (background >= 0 && nextBackground < 0) || (foreground >= 0 && nextForeground < 0))
            {
                sink.Write(Reset);
                foreground = -1;
                background = -1;
                fontStyle = FontStyle.None;
            }

            if (nextForeground != foreground && nextForeground >= 0)
            {
                WriteColor(38, nextForeground, ref sink);
                styled = true;
            }

            if (nextBackground != background && nextBackground >= 0)
            {
                WriteColor(48, nextBackground, ref sink);
                styled = true;
            }

            var added = nextFontStyle & ~fontStyle;
            if (added != 0)
            {
                WriteFontStyle(added, ref sink);
                styled = true;
            }

            foreground = nextForeground;
            background = nextBackground;
            fontStyle = nextFontStyle;
            sink.Write(line[start..end]);
        }

        if (tokens.IsEmpty)
        {
            sink.Write(line);
        }

        if (styled)
        {
            sink.Write(Reset);
        }
    }

    private static void WriteColor<TSink>(int code, int rgb, ref TSink sink)
        where TSink : struct, ICharSink
    {
        sink.Write("\u001b[");
        sink.WriteInt32(code);
        sink.Write(";2;");
        sink.WriteInt32((rgb >> 16) & 0xFF);
        sink.Write(';');
        sink.WriteInt32((rgb >> 8) & 0xFF);
        sink.Write(';');
        sink.WriteInt32(rgb & 0xFF);
        sink.Write('m');
    }

    private static void WriteFontStyle<TSink>(FontStyle added, ref TSink sink)
        where TSink : struct, ICharSink
    {
        if ((added & FontStyle.Bold) != 0)
        {
            sink.Write("\u001b[1m");
        }

        if ((added & FontStyle.Italic) != 0)
        {
            sink.Write("\u001b[3m");
        }

        if ((added & FontStyle.Underline) != 0)
        {
            sink.Write("\u001b[4m");
        }

        if ((added & FontStyle.Strikethrough) != 0)
        {
            sink.Write("\u001b[9m");
        }
    }
}
