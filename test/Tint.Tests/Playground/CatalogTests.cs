using Tint.Grammars;
using Tint.Playground;
using Tint.Themes;
using Xunit;

namespace Tint.Tests.Playground;

public sealed class CatalogTests
{
    [Fact]
    public void Every_grammar_with_a_language_is_listed_once_and_sorted_by_label()
    {
        var languages = LanguageCatalog.Build(TintGrammars.All);

        var expected = TintGrammars.All.Where(g => g.Info.LanguageId is not null).Select(g => g.Info.LanguageId).Distinct().Order().ToArray();
        Assert.Equal(expected, languages.Select(l => l.Id).Order().ToArray());
        Assert.Equal(languages.Select(l => l.Label).Order(StringComparer.OrdinalIgnoreCase), languages.Select(l => l.Label));
        Assert.Contains(languages, l => l is { Id: "csharp", Label: "C#", ScopeName: "source.cs" });
    }

    [Fact]
    public void Grammars_without_a_language_are_left_out()
    {
        var languages = LanguageCatalog.Build(TintGrammars.All);

        Assert.DoesNotContain(languages, l => l.ScopeName == "text.html.basic");
        Assert.DoesNotContain(languages, l => l.ScopeName == "source.js.regexp");
    }

    [Fact]
    public void A_language_without_aliases_is_labeled_by_its_id()
    {
        var grammar = new TestGrammar("source.x", new GrammarInfo { LanguageId = "xlang" });

        Assert.Equal("xlang", Assert.Single(LanguageCatalog.Build([grammar])).Label);
    }

    [Fact]
    public void Themes_are_grouped_dark_first_by_their_declared_type_or_their_name()
    {
        var themes = ThemeCatalog.Build(TintThemes.All);

        Assert.Equal(TintThemes.All.Count, themes.Count);
        Assert.Equal(themes.OrderBy(t => t.Kind).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase), themes);
        Assert.Equal(ThemeKind.Dark, themes.Single(t => t.Id == "dark-plus").Kind);
        Assert.Equal(ThemeKind.Light, themes.Single(t => t.Id == "light-plus").Kind);
        Assert.Equal(ThemeKind.Light, themes.Single(t => t.Id == "quiet-light").Kind);
        Assert.Equal(ThemeKind.Light, themes.Single(t => t.Id == "solarized-light").Kind);
        Assert.Equal(ThemeKind.Dark, themes.Single(t => t.Id == "red").Kind);
        Assert.Equal(ThemeKind.Light, themes.Single(t => t.Id == "light-high-contrast").Kind);
        Assert.Equal(themes.Count, themes.Select(t => t.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("dark", "Anything", ThemeKind.Dark)]
    [InlineData("hcDark", "Light-ish", ThemeKind.Dark)]
    [InlineData("light", "Dark-ish", ThemeKind.Light)]
    [InlineData("hcLight", "x", ThemeKind.Light)]
    [InlineData(null, "Solarized (light)", ThemeKind.Light)]
    [InlineData(null, "Abyss", ThemeKind.Dark)]
    public void A_theme_kind_comes_from_its_type_then_its_name(string? type, string name, ThemeKind kind)
        => Assert.Equal(kind, ThemeCatalog.GetKind(type, name));

    [Theory]
    [InlineData("Dark+", "dark-plus")]
    [InlineData("Light+", "light-plus")]
    [InlineData("Dark (Visual Studio)", "dark-visual-studio")]
    [InlineData("Solarized (dark)", "solarized-dark")]
    [InlineData("2026 Dark", "2026-dark")]
    [InlineData("C++", "c-plus-plus")]
    [InlineData("!!!", "theme")]
    public void Theme_ids_are_slugs_of_their_names(string name, string id) => Assert.Equal(id, ThemeCatalog.Slugify(name));

    [Fact]
    public void Themes_without_a_name_are_named_after_their_class()
    {
        var names = ThemeCatalog.Build(TintThemes.All).Select(t => t.Name).ToArray();

        Assert.Contains("Monokai", names);
        Assert.Contains("Monokai Dimmed", names);
        Assert.Contains("Tomorrow Night Blue", names);
        Assert.Equal("Dark Plus", ThemeCatalog.NameFromClass("DarkPlusTheme"));
        Assert.Equal("Theme", ThemeCatalog.NameFromClass("Theme"));
    }

    [Fact]
    public void Theme_names_that_slug_alike_get_distinct_ids()
    {
        var themes = ThemeCatalog.Build([new RawTheme([], "Same"), new RawTheme([], "same")]);

        Assert.Equal(["same", "same-2"], themes.Select(t => t.Id).Order());
    }

    private sealed class TestGrammar(string scopeName, GrammarInfo info) : IGrammarSource
    {
        public string ScopeName => scopeName;

        public GrammarInfo Info => info;

        public RawGrammar GetGrammar() => new(scopeName, []);
    }
}
