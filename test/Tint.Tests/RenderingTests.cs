using System.Buffers;
using System.Text;
using Tint.Loading;
using Tint.Rendering;
using Tint.Themes;
using Xunit;

namespace Tint.Tests;

public sealed class RenderingTests
{
    // A grammar whose scopes map one to one to the styles of the themes below, so every style path is reachable.
    private const string TestGrammar = """
        {
          "scopeName": "source.render",
          "patterns": [
            { "match": "//.*", "name": "comment.line.render" },
            { "match": "\\b(?:if|else)\\b", "name": "keyword.control.render" },
            { "match": "\"[^\"]*\"", "name": "string.quoted.render" },
            { "match": "\\b[A-Z]\\w*", "name": "entity.name.type.render" },
            { "match": "\\b\\d+\\b", "name": "constant.numeric.render" },
            { "match": "~\\w+", "name": "markup.deleted.render" },
            {
              "match": "(<)(\\w+)(>)",
              "captures": {
                "1": { "name": "punctuation.definition.tag.render" },
                "2": { "name": "entity.name.tag.render" },
                "3": { "name": "punctuation.definition.tag.render" }
              }
            }
          ]
        }
        """;

    private static readonly Lazy<GrammarRegistry> s_registry = new(() =>
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(Fixtures.RepositoryRoot, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json")));
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes(TestGrammar)));
        return registry;
    });

    private static readonly Lazy<Theme> s_darkPlus = new(() => LoadTheme("dark_plus.json"));
    private static readonly Lazy<Theme> s_lightPlus = new(() => LoadTheme("light_plus.json"));

    // Light: every font style, a background, and a numeric color equal to the default foreground.
    private static readonly Lazy<Theme> s_light = new(() => Theme.FromSource(new RawTheme(
    [
        new RawThemeRule { Foreground = "#111111", Background = "#FAFAFA" },
        new RawThemeRule { Scopes = ["comment"], Foreground = "#008000", FontStyle = "italic" },
        new RawThemeRule { Scopes = ["keyword"], Foreground = "#0000FF", FontStyle = "bold" },
        new RawThemeRule { Scopes = ["string"], Foreground = "#A31515", Background = "#FFEEEE", FontStyle = "underline" },
        new RawThemeRule { Scopes = ["entity.name.type"], FontStyle = "bold" },
        new RawThemeRule { Scopes = ["constant.numeric"], Foreground = "#111111" },
        new RawThemeRule { Scopes = ["markup.deleted"], FontStyle = "strikethrough underline" },
        new RawThemeRule { Scopes = ["punctuation.definition.tag", "entity.name.tag"], Foreground = "#800000" },
    ], "Test Light")));

    // Dark: colors only, except an italic comment.
    private static readonly Lazy<Theme> s_dark = new(() => Theme.FromSource(new RawTheme(
    [
        new RawThemeRule { Foreground = "#EEEEEE", Background = "#101010" },
        new RawThemeRule { Scopes = ["comment"], Foreground = "#6A9955", FontStyle = "italic" },
        new RawThemeRule { Scopes = ["keyword"], Foreground = "#569CD6" },
        new RawThemeRule { Scopes = ["string"], Foreground = "#CE9178" },
        new RawThemeRule { Scopes = ["entity.name.type"], Foreground = "#4EC9B0" },
    ], "Test Dark")));

    private const string CSharpSnippet = "// Greets\npublic class A<T> { string s = \"<&>\"; }\n";

    [Fact]
    public void RendersCSharpWithOneTheme()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.cs", s_darkPlus.Value);
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#1e1e1e;color:#d4d4d4\" tabindex=\"0\"><code>"
            + "<span class=\"line\"><span style=\"color:#6a9955\">// Greets</span></span>\n"
            + "<span class=\"line\"><span style=\"color:#569cd6\">public</span> <span style=\"color:#569cd6\">class</span> "
            + "<span style=\"color:#4ec9b0\">A</span>&lt;<span style=\"color:#4ec9b0\">T</span>&gt; { "
            + "<span style=\"color:#569cd6\">string</span> <span style=\"color:#9cdcfe\">s</span> = "
            + "<span style=\"color:#ce9178\">\"&lt;&amp;&gt;\"</span>; }</span></code></pre>",
            highlighter.ToHtml(CSharpSnippet));
    }

    [Fact]
    public void RendersCSharpWithLightAndDarkThemes()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.cs", s_lightPlus.Value, s_darkPlus.Value);
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#ffffff;--tint-dark-bg:#1e1e1e;color:#000000;--tint-dark:#d4d4d4\" tabindex=\"0\"><code>"
            + "<span class=\"line\"><span style=\"color:#008000;--tint-dark:#6a9955\">// Greets</span></span>\n"
            + "<span class=\"line\"><span style=\"color:#0000ff;--tint-dark:#569cd6\">public</span> <span style=\"color:#0000ff;--tint-dark:#569cd6\">class</span> "
            + "<span style=\"color:#267f99;--tint-dark:#4ec9b0\">A</span>&lt;<span style=\"color:#267f99;--tint-dark:#4ec9b0\">T</span>&gt; { "
            + "<span style=\"color:#0000ff;--tint-dark:#569cd6\">string</span> <span style=\"color:#001080;--tint-dark:#9cdcfe\">s</span> = "
            + "<span style=\"color:#a31515;--tint-dark:#ce9178\">\"&lt;&amp;&gt;\"</span>; }</span></code></pre>",
            highlighter.ToHtml(CSharpSnippet));
    }

    [Fact]
    public void WritesEveryFontStyleAndBackground()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value);
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code><span class=\"line\">"
            + "<span style=\"color:#0000ff;font-weight:bold\">if</span> "
            + "<span style=\"color:#a31515;background-color:#ffeeee;text-decoration:underline\">\"x\"</span> "
            + "<span style=\"font-weight:bold\">Name</span> "
            + "<span style=\"text-decoration:underline line-through\">~gone</span> "
            + "<span style=\"color:#008000;font-style:italic\">// done</span>"
            + "</span></code></pre>",
            highlighter.ToHtml("if \"x\" Name ~gone // done"));
    }

    [Fact]
    public void EscapesMarkupCharacters()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value);
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code><span class=\"line\">"
            + "a &amp;&amp; b &lt; c &gt; d <span style=\"color:#a31515;background-color:#ffeeee;text-decoration:underline\">\"&lt;/span&gt;&amp;amp;\"</span>"
            + "</span></code></pre>",
            highlighter.ToHtml("a && b < c > d \"</span>&amp;\""));
    }

    [Fact]
    public void MergesAdjacentTokensWithTheSameStyle()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value);

        // Three capture tokens with one color are one span; a number colored like the default text is no span.
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code><span class=\"line\">"
            + "x <span style=\"color:#800000\">&lt;div&gt;</span> 42 y"
            + "</span></code></pre>",
            highlighter.ToHtml("x <div> 42 y"));
    }

    [Fact]
    public void WritesOtherThemesAsVariablesWithInheritForMissingStyles()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value, s_dark.Value);
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#fafafa;--tint-dark-bg:#101010;color:#111111;--tint-dark:#eeeeee\" tabindex=\"0\"><code><span class=\"line\">"
            + "<span style=\"color:#0000ff;font-weight:bold;--tint-dark:#569cd6;--tint-dark-font-weight:inherit\">if</span> "
            + "<span style=\"color:#a31515;background-color:#ffeeee;text-decoration:underline;--tint-dark:#ce9178;--tint-dark-bg:inherit;--tint-dark-text-decoration:inherit\">\"x\"</span> "
            + "<span style=\"color:inherit;font-weight:bold;--tint-dark:#4ec9b0;--tint-dark-font-weight:inherit\">Name</span> "
            + "<span style=\"color:#008000;font-style:italic;--tint-dark:#6a9955;--tint-dark-font-style:italic\">// x</span>"
            + "</span></code></pre>",
            highlighter.ToHtml("if \"x\" Name // x"));
    }

    [Fact]
    public void NamesAdditionalThemesAndVariablePrefix()
    {
        var renderer = new HtmlRenderer(s_light.Value, [new KeyValuePair<string, Theme>("night", s_dark.Value)], new HtmlRendererOptions { CssVariablePrefix = "--code-", PreClass = "code \"x\"" });
        var tokenizers = new[] { s_registry.Value.CreateTokenizer("source.render", s_light.Value), s_registry.Value.CreateTokenizer("source.render", s_dark.Value) };
        Assert.Equal(
            "<pre class=\"code &quot;x&quot;\" style=\"background-color:#fafafa;--code-night-bg:#101010;color:#111111;--code-night:#eeeeee\" tabindex=\"0\"><code><span class=\"line\">"
            + "<span style=\"color:inherit;font-weight:bold;--code-night:#4ec9b0;--code-night-font-weight:inherit\">A</span>"
            + "</span></code></pre>",
            renderer.Render("A", tokenizers));
    }

    [Fact]
    public void RejectsInvalidThemeSetups()
    {
        Assert.Throws<ArgumentException>(() => new HtmlRenderer(s_light.Value, [new KeyValuePair<string, Theme>("a b", s_dark.Value)]));
        Assert.Throws<ArgumentException>(() => new HtmlRenderer(s_light.Value, [new("x", s_dark.Value), new("x", s_dark.Value)]));
        Assert.Throws<ArgumentException>(() => new HtmlRenderer(s_light.Value, s_dark.Value, new HtmlRendererOptions { StyleMode = HtmlStyleMode.CssClasses }));

        var renderer = new HtmlRenderer(s_light.Value);
        Assert.Throws<ArgumentException>(() => renderer.Render("x", s_registry.Value.CreateTokenizer("source.render", s_dark.Value)));
        Assert.Throws<ArgumentException>(() => new AnsiRenderer(s_light.Value).Render("x", s_registry.Value.CreateTokenizer("source.render", s_dark.Value)));
    }

    [Fact]
    public void WritesClassesAndTheirStyleSheet()
    {
        var options = new HighlighterOptions { Html = new HtmlRendererOptions { StyleMode = HtmlStyleMode.CssClasses } };
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value, options);
        var colors = s_light.Value.ColorMap;
        Assert.Equal(
            "<pre class=\"tint\" tabindex=\"0\"><code><span class=\"line\">"
            + $"<span class=\"tint-fg-{colors.GetId("#0000FF")} tint-b\">if</span> "
            + $"<span class=\"tint-fg-{colors.GetId("#A31515")} tint-bg-{colors.GetId("#FFEEEE")} tint-u\">\"x\"</span> "
            + "<span class=\"tint-u tint-s\">~gone</span> "
            + $"<span class=\"tint-fg-{colors.GetId("#008000")} tint-i\">// c</span>"
            + "</span></code></pre>",
            highlighter.ToHtml("if \"x\" ~gone // c"));

        var expected = new StringBuilder(".tint { background-color: #fafafa; color: #111111; }\n");
        for (var id = 1; id < colors.Count; id++)
        {
            expected.Append($".tint .tint-fg-{id} {{ color: {colors.GetColor(id).ToLowerInvariant()}; }}\n");
        }

        for (var id = 1; id < colors.Count; id++)
        {
            expected.Append($".tint .tint-bg-{id} {{ background-color: {colors.GetColor(id).ToLowerInvariant()}; }}\n");
        }

        expected.Append(".tint .tint-i { font-style: italic; }\n")
            .Append(".tint .tint-b { font-weight: bold; }\n")
            .Append(".tint .tint-u { text-decoration: underline; }\n")
            .Append(".tint .tint-s { text-decoration: line-through; }\n")
            .Append(".tint .tint-u.tint-s { text-decoration: underline line-through; }\n");
        Assert.Equal(expected.ToString(), highlighter.GetStyleSheet());
    }

    [Fact]
    public void StyleSheetOfInlineModeHoldsThePreColors()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.cs", s_darkPlus.Value);
        Assert.Equal(".tint { background-color: #1e1e1e; color: #d4d4d4; }\n", highlighter.GetStyleSheet());
    }

    [Fact]
    public void WritesLineNumbersWhenAsked()
    {
        var options = new HighlighterOptions { Html = new HtmlRendererOptions { LineNumbers = true, FirstLineNumber = 9 } };
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value, options);
        Assert.Equal(
            "<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code>"
            + "<span class=\"line\" data-line=\"9\">a</span>\n<span class=\"line\" data-line=\"10\">b</span></code></pre>",
            highlighter.ToHtml("a\nb"));
    }

    [Theory]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\r\nb\r\n", "a\nb")]
    [InlineData("a\nb\n", "a\nb")]
    [InlineData("a\n\nb", "a\n\nb")]
    [InlineData("a\n\n", "a\n")]
    public void SplitsLinesAtLineFeedsAndCrLf(string code, string equivalent)
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value);
        Assert.Equal(Html(equivalent), highlighter.ToHtml(code));
    }

    [Fact]
    public void RendersEmptyCodeAsAnEmptyBlock()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value);
        Assert.Equal("<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code></code></pre>", highlighter.ToHtml(string.Empty));
        Assert.Equal("<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code><span class=\"line\"></span></code></pre>", highlighter.ToHtml("\n"));
        Assert.Equal(string.Empty, highlighter.ToAnsi(string.Empty));
    }

    [Fact]
    public void WritesTheSameHtmlToEveryOutput()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.cs", s_lightPlus.Value, s_darkPlus.Value);
        var code = File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot, "benchmarks", "samples", "sample.cs"));
        var expected = highlighter.ToHtml(code);

        var writer = new StringWriter();
        highlighter.ToHtml(code, writer);
        Assert.Equal(expected, writer.ToString());

        var buffer = new ArrayBufferWriter<char>();
        highlighter.ToHtml(code, buffer);
        Assert.Equal(expected, buffer.WrittenSpan.ToString());

        var builder = new StringBuilder();
        highlighter.ToHtml(code, builder);
        Assert.Equal(expected, builder.ToString());
    }

    [Fact]
    public void WritesTrueColorSequences()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value);
        const string Fg = "\e[38;2;17;17;17m";
        Assert.Equal(
            "\e[38;2;0;0;255m\e[1mif\e[0m" + Fg + " "
            + "\e[38;2;163;21;21m\e[4m\"x\"\e[0m" + Fg + " \e[1mName\e[0m" + Fg + " \e[4m\e[9m~gone\e[0m" + Fg + " "
            + "\e[38;2;0;128;0m\e[3m// c\e[0m\n"
            + Fg + "x \e[38;2;128;0;0m<div>" + Fg + " 42\e[0m\n",
            highlighter.ToAnsi("if \"x\" Name ~gone // c\r\nx <div> 42\n"));
    }

    [Fact]
    public void WritesBackgroundsWhenAsked()
    {
        var options = new HighlighterOptions { Ansi = new AnsiRendererOptions { Background = true } };
        var highlighter = s_registry.Value.CreateHighlighter("source.render", s_light.Value, options);
        Assert.Equal(
            "\e[38;2;17;17;17m\e[48;2;250;250;250ma \e[38;2;163;21;21m\e[48;2;255;238;238m\e[4m\"x\"\e[0m\e[38;2;17;17;17m\e[48;2;250;250;250m b\e[0m",
            highlighter.ToAnsi("a \"x\" b"));
    }

    [Fact]
    public void WritesTheSameAnsiToEveryOutput()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.cs", s_darkPlus.Value);
        var code = File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot, "benchmarks", "samples", "sample.cs"));
        var expected = highlighter.ToAnsi(code);

        var writer = new StringWriter();
        highlighter.ToAnsi(code, writer);
        Assert.Equal(expected, writer.ToString());

        var buffer = new ArrayBufferWriter<char>();
        highlighter.ToAnsi(code, buffer);
        Assert.Equal(expected, buffer.WrittenSpan.ToString());

        var builder = new StringBuilder();
        highlighter.ToAnsi(code, builder);
        Assert.Equal(expected, builder.ToString());
    }

    [Fact]
    public void TokenizeResolvesTheTokenizerColors()
    {
        var registry = s_registry.Value;
        var theme = s_darkPlus.Value;
        var highlighter = registry.CreateHighlighter("source.cs", theme);
        var tokenizer = registry.CreateTokenizer("source.cs", theme);
        var lines = File.ReadAllLines(Path.Combine(Fixtures.RepositoryRoot, "benchmarks", "samples", "medium.cs"));

        var highlighted = highlighter.Tokenize(string.Join("\r\n", lines));
        Assert.Equal(lines.Length, highlighted.Length);

        var state = StateStack.Initial;
        for (var i = 0; i < lines.Length; i++)
        {
            var tokens = tokenizer.TokenizeLine(lines[i], state, out state);
            Assert.Equal(tokens.Length, highlighted[i].Length);
            for (var j = 0; j < tokens.Length; j++)
            {
                var token = tokens[j];
                var actual = highlighted[i][j];
                Assert.Equal(token.Start, actual.Start);
                Assert.Equal(token.Length, actual.Length);
                Assert.Equal(theme.ColorMap.GetColor(token.Foreground), actual.Color);
                Assert.Equal(theme.ColorMap.GetColor(token.Background), actual.BackgroundColor);
                Assert.Equal(token.FontStyle, actual.FontStyle);
                Assert.Equal(token.Scopes, actual.Scopes);
            }
        }
    }

    [Fact]
    public void HighlighterReusesItsTokenizersAcrossCalls()
    {
        var highlighter = s_registry.Value.CreateHighlighter("source.cs", s_darkPlus.Value);
        Assert.Equal(highlighter.ToHtml(CSharpSnippet), highlighter.ToHtml(CSharpSnippet));
        Assert.Equal("source.cs", highlighter.ScopeName);
        Assert.Same(s_darkPlus.Value, highlighter.Theme);
    }

    private static string Html(string lines)
    {
        var builder = new StringBuilder("<pre class=\"tint\" style=\"background-color:#fafafa;color:#111111\" tabindex=\"0\"><code>");
        var first = true;
        foreach (var line in lines.Split('\n'))
        {
            if (!first)
            {
                builder.Append('\n');
            }

            first = false;
            builder.Append("<span class=\"line\">").Append(line).Append("</span>");
        }

        return builder.Append("</code></pre>").ToString();
    }

    private static Theme LoadTheme(string file) => Theme.FromSource(ThemeLoader.Load(Path.Combine(Fixtures.RepositoryRoot, "themes", "theme-defaults", file)));
}
