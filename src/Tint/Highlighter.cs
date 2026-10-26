using System.Buffers;
using System.Text;
using Tint.Rendering;

namespace Tint;

/// <summary>
/// Highlights code with one grammar and one or more themes: HTML (<see cref="HtmlRenderer"/>), terminal text
/// (<see cref="AnsiRenderer"/>) or tokens with resolved colors. Created by
/// <see cref="GrammarRegistry.CreateHighlighter(string, Theme, HighlighterOptions?)"/>.
/// </summary>
/// <remarks>
/// Not thread-safe: a highlighter owns a <see cref="Tokenizer"/> per theme, with their scratch buffers and regex
/// scanners. Keep one per thread and reuse it; the compiled grammar behind it is shared by the registry.
/// </remarks>
public sealed class Highlighter
{
    private readonly Tokenizer[] _tokenizers;
    private readonly AnsiRenderer _ansi;

    internal Highlighter(Tokenizer[] tokenizers, IEnumerable<KeyValuePair<string, Theme>> additionalThemes, HighlighterOptions options)
    {
        _tokenizers = tokenizers;
        HtmlRenderer = new HtmlRenderer(tokenizers[0].Theme, additionalThemes, options.Html);
        _ansi = new AnsiRenderer(tokenizers[0].Theme, options.Ansi);
    }

    /// <summary>The root scope name of the grammar.</summary>
    public string ScopeName => _tokenizers[0].ScopeName;

    /// <summary>The default theme: the one <see cref="ToAnsi(string)"/> and <see cref="Tokenize(string)"/> use and HTML writes inline.</summary>
    public Theme Theme => _tokenizers[0].Theme;

    /// <summary>The themes HTML writes as CSS variables, by name.</summary>
    public IReadOnlyList<KeyValuePair<string, Theme>> AdditionalThemes => HtmlRenderer.AdditionalThemes;

    /// <summary>The HTML renderer, with its options.</summary>
    public HtmlRenderer HtmlRenderer { get; }

    /// <summary>The terminal renderer, with its options.</summary>
    public AnsiRenderer AnsiRenderer => _ansi;

    /// <summary>
    /// Renders code as HTML. Lines end at <c>\n</c> or <c>\r\n</c>; a newline at the very end does not start an empty
    /// line.
    /// </summary>
    public string ToHtml(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return HtmlRenderer.Render(code, _tokenizers);
    }

    /// <summary>Renders code as HTML to a writer.</summary>
    public void ToHtml(ReadOnlySpan<char> code, TextWriter writer) => HtmlRenderer.Render(code, _tokenizers, writer);

    /// <summary>Renders code as HTML to a buffer writer.</summary>
    public void ToHtml(ReadOnlySpan<char> code, IBufferWriter<char> writer) => HtmlRenderer.Render(code, _tokenizers, writer);

    /// <summary>Renders code as HTML to a string builder.</summary>
    public void ToHtml(ReadOnlySpan<char> code, StringBuilder builder) => HtmlRenderer.Render(code, _tokenizers, builder);

    /// <summary>The CSS of the HTML output; see <see cref="HtmlRenderer.GetStyleSheet"/>.</summary>
    public string GetStyleSheet() => HtmlRenderer.GetStyleSheet();

    /// <summary>
    /// Renders code with 24-bit terminal colors in the default theme. Lines end at <c>\n</c> or <c>\r\n</c> and are
    /// written with <c>\n</c>; the output ends with a newline when the code does.
    /// </summary>
    public string ToAnsi(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _ansi.Render(code, _tokenizers[0]);
    }

    /// <summary>Renders code with terminal colors to a writer.</summary>
    public void ToAnsi(ReadOnlySpan<char> code, TextWriter writer) => _ansi.Render(code, _tokenizers[0], writer);

    /// <summary>Renders code with terminal colors to a buffer writer.</summary>
    public void ToAnsi(ReadOnlySpan<char> code, IBufferWriter<char> writer) => _ansi.Render(code, _tokenizers[0], writer);

    /// <summary>Renders code with terminal colors to a string builder.</summary>
    public void ToAnsi(ReadOnlySpan<char> code, StringBuilder builder) => _ansi.Render(code, _tokenizers[0], builder);

    /// <summary>
    /// Tokenizes code with the default theme: one array of tokens per line, lines split as <see cref="ToHtml(string)"/>
    /// splits them, token offsets relative to their line.
    /// </summary>
    public HighlightedToken[][] Tokenize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var tokenizer = _tokenizers[0];
        var colors = tokenizer.Theme.ColorMap;
        var lines = new List<HighlightedToken[]>();
        var state = StateStack.Initial;
        var reader = new LineReader(code);
        while (reader.TryRead(out var line))
        {
            var tokens = tokenizer.TokenizeLine(line, state, out state);
            var result = new HighlightedToken[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                result[i] = new HighlightedToken(token.Start, token.Length, colors.GetColor(token.Foreground), colors.GetColor(token.Background), token.FontStyle, token.Scopes);
            }

            lines.Add(result);
        }

        return [.. lines];
    }
}
