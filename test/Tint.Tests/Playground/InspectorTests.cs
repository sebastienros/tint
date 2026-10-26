using Tint.Grammars;
using Tint.Playground;
using Tint.Themes;
using Xunit;

namespace Tint.Tests.Playground;

public sealed class InspectorTests
{
    private static readonly Lazy<PlaygroundEngine> s_engine = new(() => new PlaygroundEngine(TintGrammars.All, TintThemes.All));

    private static PlaygroundEngine Engine => s_engine.Value;

    private static TokenMap Map(string code, string language = "csharp")
        => Engine.Highlight(Engine.FindLanguage(language)!, Engine.FindTheme("dark-plus")!, null, code).Tokens;

    [Fact]
    public void An_offset_finds_the_token_under_it()
    {
        const string code = "var x = 1;\nreturn x;";
        var map = Map(code);

        var hit = map.Find(code.IndexOf("return", StringComparison.Ordinal) + 2)!.Value;

        Assert.Equal(1, hit.Line);
        Assert.Equal("return", code.Substring(hit.Offset, hit.Token.Length));
        Assert.Contains("keyword.control.flow.return.cs", hit.Token.Scopes.GetScopeNames());
        Assert.Equal("var", code.Substring(map.Find(0)!.Value.Offset, map.Find(0)!.Value.Token.Length));
    }

    [Fact]
    public void Offsets_after_a_crlf_line_are_mapped_to_the_next_line()
    {
        const string code = "int a;\r\nint b;\r\n";
        var map = Map(code);

        var hit = map.Find(code.IndexOf('b'))!.Value;

        Assert.Equal(2, map.LineCount);
        Assert.Equal(1, hit.Line);
        Assert.Equal("b", code.Substring(hit.Offset, hit.Token.Length));
        Assert.Null(map.Find(code.IndexOf('\r')));
        Assert.Null(map.Find(code.Length));
    }

    [Fact]
    public void A_caret_at_a_line_end_takes_the_token_before_it()
    {
        const string code = "x++;\n\nfoo";
        var map = Map(code);

        Assert.Null(map.Find(4));
        Assert.Equal(";", code.Substring(map.Find(4, caret: true)!.Value.Offset, 1));
        Assert.Null(map.Find(5, caret: true));
        Assert.Equal(2, map.Find(code.Length, caret: true)!.Value.Line);
        Assert.Null(map.Find(-1));
    }

    [Fact]
    public void Empty_code_has_one_empty_line()
    {
        var map = Map(string.Empty);

        Assert.Null(map.Find(0, caret: true));
    }

    [Fact]
    public void The_explanation_names_the_rule_that_colors_a_keyword()
    {
        const string code = "return x;";
        var darkPlus = Engine.FindTheme("dark-plus")!;
        var token = Map(code).Find(0)!.Value.Token;

        var explanation = Engine.GetExplainer(darkPlus).Explain(token.Scopes);

        Assert.NotNull(explanation.Foreground);
        Assert.Equal("keyword.control.flow.return.cs", explanation.Foreground.MatchedScope);
        Assert.StartsWith("keyword.control", explanation.Foreground.Selector);
        Assert.Equal(token.Color, explanation.Foreground.Foreground, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_explanation_names_the_rule_of_an_italic_comment()
    {
        var quietLight = Engine.FindTheme("quiet-light")!;
        var tokens = Engine.Highlight(Engine.FindLanguage("csharp")!, quietLight, null, "// note").Tokens;
        var token = tokens.Lines[0][^1];

        var explanation = Engine.GetExplainer(quietLight).Explain(token.Scopes);

        Assert.Equal(FontStyle.Italic, token.FontStyle & FontStyle.Italic);
        Assert.NotNull(explanation.FontStyle);
        Assert.Equal("Comments", explanation.FontStyle.Name);
        Assert.Equal("italic", explanation.FontStyle.FontStyle);
    }

    [Fact]
    public void Text_no_rule_matches_has_no_explanation()
    {
        var darkPlus = Engine.FindTheme("dark-plus")!;
        var token = Map("hello", "markdown").Find(0)!.Value.Token;

        Assert.Equal(new ThemeExplanation(null, null), Engine.GetExplainer(darkPlus).Explain(token.Scopes));
    }

    [Theory]
    [InlineData("csharp", "dark-plus")]
    [InlineData("csharp", "light-plus")]
    [InlineData("typescript", "monokai")]
    [InlineData("python", "solarized-dark")]
    [InlineData("html", "quiet-light")]
    [InlineData("markdown", "abyss")]
    [InlineData("csharp", "monokai")]
    [InlineData("csharp", "quiet-light")]
    [InlineData("rust", "kimbie-dark")]
    [InlineData("css", "dark-high-contrast")]
    [InlineData("go", "tomorrow-night-blue")]
    [InlineData("java", "red")]
    public void The_explained_style_is_the_style_the_tokenizer_resolved(string language, string themeId)
    {
        var theme = Engine.FindTheme(themeId)!;
        var explainer = Engine.GetExplainer(theme);
        var defaults = Engine.GetTheme(theme).Defaults;
        var defaultForeground = Engine.GetTheme(theme).ColorMap.GetColor(defaults.ForegroundId);
        var map = Engine.Highlight(Engine.FindLanguage(language)!, theme, null, File.ReadAllText(Path.Combine(Fixtures.Samples, language + ".sample"))).Tokens;

        var total = 0;
        var mismatches = new List<string>();
        foreach (var line in map.Lines)
        {
            foreach (var token in line)
            {
                total++;
                var explanation = explainer.Explain(token.Scopes);
                var foreground = explanation.Foreground?.Foreground ?? defaultForeground;
                if (!string.Equals(foreground, token.Color, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add($"{token.Scopes}: {token.Color} explained as {foreground}");
                }

                var fontStyle = explanation.FontStyle?.FontStyle is { } text ? ParseFontStyle(text) : defaults.FontStyle;
                if (fontStyle != token.FontStyle)
                {
                    mismatches.Add($"{token.Scopes}: {token.FontStyle} explained as {fontStyle}");
                }
            }
        }

        Assert.True(total > 0);
        Assert.True(mismatches.Count == 0, string.Join('\n', mismatches.Distinct().Take(20)));
    }

    private static FontStyle ParseFontStyle(string text)
    {
        var style = FontStyle.None;
        foreach (var segment in text.Split(' '))
        {
            style |= segment switch
            {
                "italic" => FontStyle.Italic,
                "bold" => FontStyle.Bold,
                "underline" => FontStyle.Underline,
                "strikethrough" => FontStyle.Strikethrough,
                _ => FontStyle.None,
            };
        }

        return style;
    }
}
