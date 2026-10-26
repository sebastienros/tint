using Tint.Grammars;
using Tint.Playground;
using Tint.Themes;
using Xunit;

namespace Tint.Tests.Playground;

public sealed class EngineTests
{
    private static readonly Lazy<PlaygroundEngine> s_engine = new(() => new PlaygroundEngine(TintGrammars.All, TintThemes.All));

    private static PlaygroundEngine Engine => s_engine.Value;

    private static string Sample(string language) => File.ReadAllText(Path.Combine(Fixtures.Samples, language + ".sample"));

    [Fact]
    public void Highlighting_renders_html_and_indexes_the_tokens()
    {
        var csharp = Engine.FindLanguage("csharp")!;
        var darkPlus = Engine.FindTheme("dark-plus")!;
        var code = Sample("csharp");

        var result = Engine.Highlight(csharp, darkPlus, null, code);

        Assert.StartsWith("<pre class=\"tint\" style=\"background-color:#1e1e1e;color:#d4d4d4\"", result.Html);
        Assert.Equal(code.TrimEnd('\n').Split('\n').Length, result.Tokens.LineCount);
        Assert.True(result.Tokens.TokenCount > result.Tokens.LineCount);
        Assert.True(result.TokenizeTime > TimeSpan.Zero);
    }

    [Fact]
    public void The_inspected_theme_colors_the_tokens()
    {
        var csharp = Engine.FindLanguage("csharp")!;
        var dark = Engine.FindTheme("dark-plus")!;
        var light = Engine.FindTheme("light-plus")!;

        var result = Engine.Highlight(csharp, dark, light, "return;", light);

        Assert.Contains("--tint-light:", result.Html);
        Assert.Equal("#af00db", result.Tokens.Lines[0][0].Color, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Highlighters_are_cached_per_language_and_themes()
    {
        var engine = new PlaygroundEngine(TintGrammars.All, TintThemes.All);
        var csharp = engine.FindLanguage("csharp")!;
        var json = engine.FindLanguage("json")!;
        var dark = engine.FindTheme("dark-plus")!;
        var light = engine.FindTheme("light-plus")!;

        var first = engine.GetHighlighter(csharp, dark);

        Assert.Same(first, engine.GetHighlighter(csharp, dark));
        Assert.NotSame(first, engine.GetHighlighter(csharp, dark, light));
        Assert.NotSame(first, engine.GetHighlighter(json, dark));
        Assert.Equal(3, engine.CachedHighlighterCount);
        Assert.Equal("light", Assert.Single(engine.GetHighlighter(csharp, dark, light).AdditionalThemes).Key);
    }

    [Fact]
    public void The_cache_drops_the_least_recently_used_highlighter()
    {
        var engine = new PlaygroundEngine(TintGrammars.All, TintThemes.All);
        var dark = engine.FindTheme("dark-plus")!;
        var first = engine.GetHighlighter(engine.Languages[0], dark);
        var kept = engine.GetHighlighter(engine.Languages[1], dark);

        for (var i = 2; i < PlaygroundEngine.CacheCapacity + 1; i++)
        {
            engine.GetHighlighter(engine.Languages[i], dark);
            Assert.Same(kept, engine.GetHighlighter(engine.Languages[1], dark));
        }

        Assert.Equal(PlaygroundEngine.CacheCapacity, engine.CachedHighlighterCount);
        Assert.NotSame(first, engine.GetHighlighter(engine.Languages[0], dark));
    }

    [Fact]
    public void Every_listed_language_has_a_sample_or_is_known_to_have_none()
    {
        // shiki has no sample for these (test/Tint.Tests/Fixtures/samples/README.md).
        string[] withoutSample = ["cpp_embedded_latex", "cuda-cpp", "git-commit", "git-rebase", "ignore", "markdown-math", "markdown_latex_combined", "prompt", "search-result", "snippets"];

        var missing = Engine.Languages.Select(l => l.Id).Where(id => !File.Exists(Path.Combine(Fixtures.Samples, id + ".sample"))).Order().ToArray();

        Assert.Equal(withoutSample.Order(), missing);
    }

    [Fact]
    public void The_dual_theme_css_switches_on_the_second_theme_kind()
    {
        var css = PlaygroundEngine.GetDualThemeCss(Engine.FindTheme("dark-plus")!);

        Assert.StartsWith("@media (prefers-color-scheme: dark) {\n  .tint, .tint span {\n    color: var(--tint-dark) !important;", css);
        Assert.Contains("    text-decoration: var(--tint-dark-text-decoration) !important;\n  }\n}\n", css);
        Assert.StartsWith(".alt .tint, .alt .tint span {\n  color: var(--tint-light) !important;", PlaygroundEngine.GetDualThemeCss(Engine.FindTheme("light-plus")!, ".alt"));
    }

    [Fact]
    public void Editor_colors_come_from_the_theme_ui_colors_or_its_defaults()
    {
        Assert.Equal(("#1E1E1E", "#D4D4D4"), Engine.GetEditorColors(Engine.FindTheme("dark-plus")!), new CaseInsensitiveColors());
        var plain = new PlaygroundEngine([], [new RawTheme([new RawThemeRule { Foreground = "#123456", Background = "#abcdef" }], "Plain")]);
        Assert.Equal(("#ABCDEF", "#123456"), plain.GetEditorColors(plain.Themes[0]));
    }

    private sealed class CaseInsensitiveColors : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) => 0;
    }
}
