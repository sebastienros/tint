using System.Buffers;
using System.Text;

namespace Tint.Rendering;

/// <summary>
/// Renders tokenized code as HTML in shiki's shape: a <c>pre</c> with the theme's colors, a <c>code</c>, one
/// <c>span class="line"</c> per line, and a <c>span</c> per run of tokens with the same style holding only the styles
/// that differ from the theme's defaults.
/// </summary>
/// <remarks>
/// <para>
/// With several themes (a light and a dark one, say), the first theme's colors are written inline and each other
/// theme's as CSS variables named after it, as shiki's dual themes do: <c>--tint-dark</c> for the color,
/// <c>--tint-dark-bg</c> for the background, <c>--tint-dark-font-style</c>, <c>--tint-dark-font-weight</c> and
/// <c>--tint-dark-text-decoration</c> when a theme sets them. Switch with CSS only, for example:
/// </para>
/// <code language="css">
/// @media (prefers-color-scheme: dark) {
///   .tint, .tint span {
///     color: var(--tint-dark) !important;
///     background-color: var(--tint-dark-bg) !important;
///     font-style: var(--tint-dark-font-style) !important;
///     font-weight: var(--tint-dark-font-weight) !important;
///     text-decoration: var(--tint-dark-text-decoration) !important;
///   }
/// }
/// </code>
/// <para>
/// Each theme is tokenized by its own <see cref="Tokenizer"/> and the token boundaries are merged. A renderer is
/// immutable and thread-safe; the tokenizers passed to it are not, and belong to one render at a time.
/// </para>
/// </remarks>
public sealed class HtmlRenderer
{
    private static readonly SearchValues<char> s_escaped = SearchValues.Create("&<>");

    private readonly ThemePalette[] _palettes;
    private readonly string[] _variablePrefixes;
    private readonly string _preStart;
    private readonly string _classPrefix;

    /// <summary>Creates a renderer for one theme.</summary>
    public HtmlRenderer(Theme theme, HtmlRendererOptions? options = null)
        : this(theme, [], options)
    {
    }

    /// <summary>Creates a renderer for a light theme, written inline, and a dark theme, written as <c>--tint-dark</c> variables.</summary>
    public HtmlRenderer(Theme light, Theme dark, HtmlRendererOptions? options = null)
        : this(light, [new KeyValuePair<string, Theme>("dark", dark ?? throw new ArgumentNullException(nameof(dark)))], options)
    {
    }

    /// <summary>
    /// Creates a renderer for a default theme, written inline, and named additional themes, written as CSS variables.
    /// </summary>
    /// <param name="defaultTheme">The theme whose colors are written inline.</param>
    /// <param name="additionalThemes">Up to seven more themes by name; names are CSS identifiers (letters, digits, <c>-</c> and <c>_</c>).</param>
    /// <param name="options">Rendering settings, or null for <see cref="HtmlRendererOptions.Default"/>.</param>
    /// <exception cref="ArgumentException">A name is not a CSS identifier or appears twice, there are more than eight themes, or <see cref="HtmlStyleMode.CssClasses"/> is asked with several themes.</exception>
    public HtmlRenderer(Theme defaultTheme, IEnumerable<KeyValuePair<string, Theme>> additionalThemes, HtmlRendererOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(defaultTheme);
        ArgumentNullException.ThrowIfNull(additionalThemes);
        Options = options ?? HtmlRendererOptions.Default;

        var palettes = new List<ThemePalette> { new(defaultTheme) };
        var names = new List<string>();
        var themes = new List<KeyValuePair<string, Theme>>();
        foreach (var (name, theme) in additionalThemes)
        {
            ArgumentNullException.ThrowIfNull(theme, nameof(additionalThemes));
            if (string.IsNullOrEmpty(name) || !IsCssIdentifier(name))
            {
                throw new ArgumentException($"The theme name '{name}' is not a CSS identifier.", nameof(additionalThemes));
            }

            if (names.Contains(name))
            {
                throw new ArgumentException($"The theme name '{name}' appears twice.", nameof(additionalThemes));
            }

            names.Add(name);
            themes.Add(new KeyValuePair<string, Theme>(name, theme));
            palettes.Add(new ThemePalette(theme));
        }

        if (palettes.Count > ThemeSet.MaxThemes)
        {
            throw new ArgumentException($"A renderer takes at most {ThemeSet.MaxThemes} themes.", nameof(additionalThemes));
        }

        if (Options.StyleMode == HtmlStyleMode.CssClasses && palettes.Count > 1)
        {
            throw new ArgumentException("CSS class mode supports one theme; several themes are written as CSS variables with inline styles.", nameof(options));
        }

        if (!IsCssIdentifier(Options.ClassPrefix) || !Options.CssVariablePrefix.StartsWith("--", StringComparison.Ordinal) || !IsCssIdentifier(Options.CssVariablePrefix))
        {
            throw new ArgumentException("ClassPrefix must be a CSS identifier and CssVariablePrefix one that starts with '--'.", nameof(options));
        }

        DefaultTheme = defaultTheme;
        AdditionalThemes = themes;
        _palettes = [.. palettes];
        _variablePrefixes = [.. names.Select(n => Options.CssVariablePrefix + n)];
        _classPrefix = Options.ClassPrefix;
        _preStart = BuildPreStart();
    }

    /// <summary>The theme written inline.</summary>
    public Theme DefaultTheme { get; }

    /// <summary>The themes written as CSS variables, by name.</summary>
    public IReadOnlyList<KeyValuePair<string, Theme>> AdditionalThemes { get; }

    /// <summary>The rendering settings.</summary>
    public HtmlRendererOptions Options { get; }

    /// <summary>The number of themes, and so of tokenizers a render takes.</summary>
    public int ThemeCount => _palettes.Length;

    /// <summary>
    /// Renders code to a writer. Lines end at <c>\n</c> or <c>\r\n</c>; a newline at the very end does not start an
    /// empty line.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="tokenizers">One tokenizer per theme, in order (<see cref="DefaultTheme"/> first), over the same grammar. Each starts from <see cref="StateStack.Initial"/>.</param>
    /// <param name="writer">Where the HTML goes.</param>
    /// <exception cref="ArgumentException">The tokenizers do not match the renderer's themes.</exception>
    public void Render(ReadOnlySpan<char> code, ReadOnlySpan<Tokenizer> tokenizers, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new TextWriterSink(writer);
        Render(code, tokenizers, ref sink);
    }

    /// <inheritdoc cref="Render(ReadOnlySpan{char}, ReadOnlySpan{Tokenizer}, TextWriter)"/>
    public void Render(ReadOnlySpan<char> code, ReadOnlySpan<Tokenizer> tokenizers, IBufferWriter<char> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new BufferWriterSink(writer);
        Render(code, tokenizers, ref sink);
    }

    /// <inheritdoc cref="Render(ReadOnlySpan{char}, ReadOnlySpan{Tokenizer}, TextWriter)"/>
    public void Render(ReadOnlySpan<char> code, ReadOnlySpan<Tokenizer> tokenizers, StringBuilder writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var sink = new StringBuilderSink(writer);
        Render(code, tokenizers, ref sink);
    }

    /// <summary>Renders code to a string.</summary>
    /// <inheritdoc cref="Render(ReadOnlySpan{char}, ReadOnlySpan{Tokenizer}, TextWriter)"/>
    public string Render(ReadOnlySpan<char> code, ReadOnlySpan<Tokenizer> tokenizers)
    {
        var sink = new PooledCharSink((code.Length * 3) + 256);
        try
        {
            Render(code, tokenizers, ref sink);
            return sink.ToString();
        }
        finally
        {
            sink.Dispose();
        }
    }

    /// <summary>Renders code with one tokenizer, for a renderer with one theme.</summary>
    /// <inheritdoc cref="Render(ReadOnlySpan{char}, ReadOnlySpan{Tokenizer}, TextWriter)"/>
    public string Render(ReadOnlySpan<char> code, Tokenizer tokenizer) => Render(code, new ReadOnlySpan<Tokenizer>(in tokenizer));

    /// <summary>
    /// The CSS rules for <see cref="HtmlStyleMode.CssClasses"/> mode: the <c>pre</c> colors and one rule per color of
    /// the theme and per font style, scoped to the <c>pre</c> class. In inline mode, only the <c>pre</c> rule.
    /// </summary>
    public string GetStyleSheet()
    {
        var palette = _palettes[0];
        var pre = "." + Options.PreClass;
        var builder = new StringBuilder();
        builder.Append(pre).Append(" { background-color: ").Append(palette.DefaultBackgroundCss).Append("; color: ").Append(palette.DefaultForegroundCss).Append("; }\n");
        if (Options.StyleMode != HtmlStyleMode.CssClasses)
        {
            return builder.ToString();
        }

        for (var id = 1; id < palette.CssColors.Length; id++)
        {
            builder.Append(pre).Append(" .").Append(_classPrefix).Append("fg-").Append(id).Append(" { color: ").Append(palette.CssColors[id]).Append("; }\n");
        }

        for (var id = 1; id < palette.CssColors.Length; id++)
        {
            builder.Append(pre).Append(" .").Append(_classPrefix).Append("bg-").Append(id).Append(" { background-color: ").Append(palette.CssColors[id]).Append("; }\n");
        }

        builder.Append(pre).Append(" .").Append(_classPrefix).Append("i { font-style: italic; }\n");
        builder.Append(pre).Append(" .").Append(_classPrefix).Append("b { font-weight: bold; }\n");
        builder.Append(pre).Append(" .").Append(_classPrefix).Append("u { text-decoration: underline; }\n");
        builder.Append(pre).Append(" .").Append(_classPrefix).Append("s { text-decoration: line-through; }\n");
        builder.Append(pre).Append(" .").Append(_classPrefix).Append("u.").Append(_classPrefix).Append("s { text-decoration: underline line-through; }\n");
        return builder.ToString();
    }

    internal void Render<TSink>(ReadOnlySpan<char> code, ReadOnlySpan<Tokenizer> tokenizers, ref TSink sink)
        where TSink : struct, ICharSink
    {
        ThemeSet.Validate(tokenizers, _palettes, nameof(tokenizers));
        var themeCount = _palettes.Length;
        var lines = new LineTokensBuffer();
        var states = new StateBuffer();
        Span<uint> pending = stackalloc uint[themeCount];
        Span<uint> current = stackalloc uint[themeCount];
        for (var i = 0; i < themeCount; i++)
        {
            lines[i] = new LineTokens();
            states[i] = StateStack.Initial;
        }

        try
        {
            sink.Write(_preStart);
            var reader = new LineReader(code);
            var lineNumber = Options.FirstLineNumber;
            var first = true;
            while (reader.TryRead(out var line))
            {
                if (!first)
                {
                    sink.Write('\n');
                }

                first = false;
                for (var i = 0; i < themeCount; i++)
                {
                    lines[i].Load(tokenizers[i].TokenizeLineEncoded(line, states[i], out states[i]));
                }

                if (Options.LineNumbers)
                {
                    sink.Write("<span class=\"line\" data-line=\"");
                    sink.WriteInt32(lineNumber++);
                    sink.Write("\">");
                }
                else
                {
                    sink.Write("<span class=\"line\">");
                }

                WriteLine(line, ref lines, pending, current, ref sink);
                sink.Write("</span>");
            }

            sink.Write("</code></pre>");
        }
        finally
        {
            for (var i = 0; i < themeCount; i++)
            {
                lines[i].Dispose();
            }
        }
    }

    // Walks the token boundaries of every theme together and writes one span per run of equal styles.
    private void WriteLine<TSink>(ReadOnlySpan<char> line, ref LineTokensBuffer lines, Span<uint> pending, Span<uint> current, ref TSink sink)
        where TSink : struct, ICharSink
    {
        var position = 0;
        var runStart = 0;
        while (position < line.Length)
        {
            var next = line.Length;
            for (var i = 0; i < current.Length; i++)
            {
                next = Math.Min(next, lines[i].Seek(position, line.Length, out var metadata));
                current[i] = _palettes[i].GetStyleKey(metadata);
            }

            if (position == 0)
            {
                current.CopyTo(pending);
            }
            else if (!current.SequenceEqual(pending))
            {
                WriteRun(line[runStart..position], pending, ref sink);
                current.CopyTo(pending);
                runStart = position;
            }

            position = next;
        }

        if (runStart < line.Length)
        {
            WriteRun(line[runStart..], pending, ref sink);
        }
    }

    private void WriteRun<TSink>(ReadOnlySpan<char> text, ReadOnlySpan<uint> styles, ref TSink sink)
        where TSink : struct, ICharSink
    {
        var styled = false;
        foreach (var style in styles)
        {
            styled |= style != 0;
        }

        if (!styled)
        {
            WriteEscaped(text, ref sink);
            return;
        }

        if (Options.StyleMode == HtmlStyleMode.CssClasses)
        {
            sink.Write("<span class=\"");
            WriteClasses(styles[0], ref sink);
        }
        else
        {
            sink.Write("<span style=\"");
            WriteStyles(styles, ref sink);
        }

        sink.Write("\">");
        WriteEscaped(text, ref sink);
        sink.Write("</span>");
    }

    private void WriteClasses<TSink>(uint style, ref TSink sink)
        where TSink : struct, ICharSink
    {
        var separator = false;
        if (StyleKey.Foreground(style) is var foreground and not 0)
        {
            WriteClass("fg-", ref separator, ref sink);
            sink.WriteInt32(foreground);
        }

        if (StyleKey.Background(style) is var background and not 0)
        {
            WriteClass("bg-", ref separator, ref sink);
            sink.WriteInt32(background);
        }

        var fontStyle = StyleKey.FontStyle(style);
        if ((fontStyle & FontStyle.Italic) != 0)
        {
            WriteClass("i", ref separator, ref sink);
        }

        if ((fontStyle & FontStyle.Bold) != 0)
        {
            WriteClass("b", ref separator, ref sink);
        }

        if ((fontStyle & FontStyle.Underline) != 0)
        {
            WriteClass("u", ref separator, ref sink);
        }

        if ((fontStyle & FontStyle.Strikethrough) != 0)
        {
            WriteClass("s", ref separator, ref sink);
        }
    }

    private void WriteClass<TSink>(string name, ref bool separator, ref TSink sink)
        where TSink : struct, ICharSink
    {
        if (separator)
        {
            sink.Write(' ');
        }

        separator = true;
        sink.Write(_classPrefix);
        sink.Write(name);
    }

    // The declarations every theme needs, theme by theme: the default theme's as properties, the others' as
    // variables. A property one theme sets and another does not is written as inherit for the other, as shiki does.
    private void WriteStyles<TSink>(ReadOnlySpan<uint> styles, ref TSink sink)
        where TSink : struct, ICharSink
    {
        var hasColor = false;
        var hasBackground = false;
        var fontStyles = FontStyle.None;
        foreach (var style in styles)
        {
            hasColor |= StyleKey.Foreground(style) != 0;
            hasBackground |= StyleKey.Background(style) != 0;
            fontStyles |= StyleKey.FontStyle(style);
        }

        var separator = false;
        for (var i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            var palette = _palettes[i];
            var prefix = i == 0 ? null : _variablePrefixes[i - 1];
            var fontStyle = StyleKey.FontStyle(style);
            if (hasColor)
            {
                var id = StyleKey.Foreground(style);
                WriteDeclaration(prefix, "color", string.Empty, id == 0 ? "inherit" : palette.GetCss(id), ref separator, ref sink);
            }

            if (hasBackground)
            {
                var id = StyleKey.Background(style);
                WriteDeclaration(prefix, "background-color", "-bg", id == 0 ? "inherit" : palette.GetCss(id), ref separator, ref sink);
            }

            if ((fontStyles & FontStyle.Italic) != 0)
            {
                WriteDeclaration(prefix, "font-style", "-font-style", (fontStyle & FontStyle.Italic) != 0 ? "italic" : "inherit", ref separator, ref sink);
            }

            if ((fontStyles & FontStyle.Bold) != 0)
            {
                WriteDeclaration(prefix, "font-weight", "-font-weight", (fontStyle & FontStyle.Bold) != 0 ? "bold" : "inherit", ref separator, ref sink);
            }

            if ((fontStyles & (FontStyle.Underline | FontStyle.Strikethrough)) != 0)
            {
                var decoration = (fontStyle & (FontStyle.Underline | FontStyle.Strikethrough)) switch
                {
                    FontStyle.Underline => "underline",
                    FontStyle.Strikethrough => "line-through",
                    FontStyle.Underline | FontStyle.Strikethrough => "underline line-through",
                    _ => "inherit",
                };
                WriteDeclaration(prefix, "text-decoration", "-text-decoration", decoration, ref separator, ref sink);
            }
        }
    }

    private static void WriteDeclaration<TSink>(string? variablePrefix, string property, string variableSuffix, string value, ref bool separator, ref TSink sink)
        where TSink : struct, ICharSink
    {
        if (separator)
        {
            sink.Write(';');
        }

        separator = true;
        if (variablePrefix is null)
        {
            sink.Write(property);
        }
        else
        {
            sink.Write(variablePrefix);
            sink.Write(variableSuffix);
        }

        sink.Write(':');
        sink.Write(value);
    }

    internal static void WriteEscaped<TSink>(ReadOnlySpan<char> text, ref TSink sink)
        where TSink : struct, ICharSink
    {
        while (true)
        {
            var index = text.IndexOfAny(s_escaped);
            if (index < 0)
            {
                sink.Write(text);
                return;
            }

            sink.Write(text[..index]);
            sink.Write(text[index] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                _ => "&gt;",
            });
            text = text[(index + 1)..];
        }
    }

    private string BuildPreStart()
    {
        var builder = new StringBuilder("<pre class=\"");
        var escaped = new StringBuilder();
        var sink = new StringBuilderSink(escaped);
        WriteEscaped(Options.PreClass, ref sink);
        builder.Append(escaped.Replace("\"", "&quot;")).Append('"');
        if (Options.StyleMode == HtmlStyleMode.InlineStyles)
        {
            builder.Append(" style=\"background-color:").Append(_palettes[0].DefaultBackgroundCss);
            for (var i = 1; i < _palettes.Length; i++)
            {
                builder.Append(';').Append(_variablePrefixes[i - 1]).Append("-bg:").Append(_palettes[i].DefaultBackgroundCss);
            }

            builder.Append(";color:").Append(_palettes[0].DefaultForegroundCss);
            for (var i = 1; i < _palettes.Length; i++)
            {
                builder.Append(';').Append(_variablePrefixes[i - 1]).Append(':').Append(_palettes[i].DefaultForegroundCss);
            }

            builder.Append('"');
        }

        builder.Append(" tabindex=\"0\"><code>");
        return builder.ToString();
    }

    private static bool IsCssIdentifier(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            {
                return false;
            }
        }

        return value.Length > 0;
    }
}
