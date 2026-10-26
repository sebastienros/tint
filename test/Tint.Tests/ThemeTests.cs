using Tint.Themes;
using Xunit;
using ParsedThemeRule = Tint.Theme.ParsedThemeRule;

namespace Tint.Tests;

// The inline cases of vscode-textmate's src/tests/themes.test.ts.
public sealed class ThemeTests
{
    private static readonly Theme s_canMatch = Create(
        Global("#F8F8F2", "#272822"),
        Rule("source, something", background: "#100000"),
        Rule(["bar", "baz"], background: "#200000"),
        Rule("source.css selector bar", fontStyle: "bold"),
        Rule("constant", "#300000", fontStyle: "italic"),
        Rule("constant.numeric", "#400000"),
        Rule("constant.numeric.hex", fontStyle: "bold"),
        Rule("constant.numeric.oct", fontStyle: "bold italic underline"),
        Rule("constant.numeric.dec", "#500000", fontStyle: ""),
        Rule("storage.object.bar", "#600000", fontStyle: ""));

    [Fact]
    public void MatchingGivesHigherPriorityToDeeperMatches()
    {
        var theme = Create(
            Global("#100000", "#200000"),
            Rule("punctuation.definition.string.begin.html", "#300000"),
            Rule("meta.tag punctuation.definition.string", "#400000"));

        Assert.Equal("#300000", Foreground(theme, "punctuation.definition.string.begin.html"));
    }

    [Fact]
    public void MatchingGivesHigherPriorityToParentMatches1()
    {
        var theme = Create(
            Global("#100000", "#200000"),
            Rule("c a", "#300000"),
            Rule("d a.b", "#400000"),
            Rule("a", "#500000"));

        Assert.Equal("#400000", Foreground(theme, "d", "a.b"));
    }

    [Fact]
    public void MatchingGivesHigherPriorityToParentMatches2()
    {
        var theme = Create(
            Global("#100000", "#200000"),
            Rule("meta.tag entity", "#300000"),
            Rule("meta.selector.css entity.name.tag", "#400000"),
            Rule("entity", "#500000"));

        Assert.Equal("#300000", Foreground(theme, "text.html.cshtml", "meta.tag.structure.any.html", "entity.name.tag.structure.any.html"));
    }

    public static TheoryData<string, string[], string> CanMatchCases => new()
    {
        { "simpleMatch1", ["source"], "bg #100000, not set" },
        { "simpleMatch2", ["source.ts"], "bg #100000, not set" },
        { "simpleMatch3", ["source.tss"], "bg #100000, not set" },
        { "simpleMatch4", ["something"], "bg #100000, not set" },
        { "simpleMatch5", ["something.ts"], "bg #100000, not set" },
        { "simpleMatch6", ["something.tss"], "bg #100000, not set" },
        { "simpleMatch7", ["baz"], "bg #200000, not set" },
        { "simpleMatch8", ["baz.ts"], "bg #200000, not set" },
        { "simpleMatch9", ["baz.tss"], "bg #200000, not set" },
        { "simpleMatch10", ["constant"], "fg #300000, italic" },
        { "simpleMatch11", ["constant.string"], "fg #300000, italic" },
        { "simpleMatch12", ["constant.hex"], "fg #300000, italic" },
        { "simpleMatch13", ["constant.numeric"], "fg #400000, italic" },
        { "simpleMatch14", ["constant.numeric.baz"], "fg #400000, italic" },
        { "simpleMatch15", ["constant.numeric.hex"], "fg #400000, bold" },
        { "simpleMatch16", ["constant.numeric.hex.baz"], "fg #400000, bold" },
        { "simpleMatch17", ["constant.numeric.oct"], "fg #400000, italic bold underline" },
        { "simpleMatch18", ["constant.numeric.oct.baz"], "fg #400000, italic bold underline" },
        { "simpleMatch19", ["constant.numeric.dec"], "fg #500000, none" },
        { "simpleMatch20", ["constant.numeric.dec.baz"], "fg #500000, none" },
        { "simpleMatch21", ["storage.object.bar"], "fg #600000, none" },
        { "simpleMatch22", ["storage.object.bar.baz"], "fg #600000, none" },
        { "simpleMatch23", ["storage.object.bart"], "not set" },
        { "simpleMatch24", ["storage.object"], "not set" },
        { "simpleMatch25", ["storage"], "not set" },
        { "defaultMatch1", [""], "not set" },
        { "defaultMatch2", ["bazz"], "not set" },
        { "defaultMatch3", ["asdfg"], "not set" },
        { "multiMatch1", ["bar"], "bg #200000, not set" },
        { "multiMatch2", ["source.css", "selector", "bar"], "bg #200000, bold" },
    };

    [Theory]
    [MemberData(nameof(CanMatchCases))]
    public void MatchingCanMatch(string name, string[] path, string expected)
    {
        _ = name;
        var result = s_canMatch.Match(path)!.Value;
        var parts = new List<string>();
        if (result.ForegroundId != 0)
        {
            parts.Add("fg " + s_canMatch.ColorMap.GetColor(result.ForegroundId));
        }

        if (result.BackgroundId != 0)
        {
            parts.Add("bg " + s_canMatch.ColorMap.GetColor(result.BackgroundId));
        }

        parts.Add(FontStyleToString(result.FontStyle));
        Assert.Equal(expected, string.Join(", ", parts));
    }

    [Fact]
    public void MatchingVSCodeIssue23460()
    {
        var theme = Create(
            Global("#aec2e0", "#14191f"),
            Rule("meta.structure.dictionary.json string.quoted.double.json", "#FF410D"),
            Rule("meta.structure.dictionary.json string.quoted.double.json", "#ffffff"),
            Rule("meta.structure.dictionary.value.json string.quoted.double.json", "#FF410D"));

        Assert.Equal("#FF410D", Foreground(theme, "source.json", "meta.structure.dictionary.json", "meta.structure.dictionary.value.json", "string.quoted.double.json"));
    }

    [Fact]
    public void ParsingCanParse()
    {
        var actual = Theme.Parse(new RawTheme(
        [
            Global("#F8F8F2", "#272822"),
            Rule("source, something", background: "#100000"),
            Rule(["bar", "baz"], background: "#010000"),
            Rule("source.css selector bar", fontStyle: "bold"),
            Rule("constant", "#ff0000", fontStyle: "italic"),
            Rule("constant.numeric", "#00ff00"),
            Rule("constant.numeric.hex", fontStyle: "bold"),
            Rule("constant.numeric.oct", fontStyle: "bold italic underline"),
            Rule("constant.numeric.bin", fontStyle: "bold strikethrough"),
            Rule("constant.numeric.dec", "#0000ff", fontStyle: ""),
            Rule("foo", "#CFA", fontStyle: ""),
        ]));

        ParsedThemeRule[] expected =
        [
            new(string.Empty, null, 0, FontStyle.NotSet, "#F8F8F2", "#272822"),
            new("source", null, 1, FontStyle.NotSet, null, "#100000"),
            new("something", null, 1, FontStyle.NotSet, null, "#100000"),
            new("bar", null, 2, FontStyle.NotSet, null, "#010000"),
            new("baz", null, 2, FontStyle.NotSet, null, "#010000"),
            new("bar", ["selector", "source.css"], 3, FontStyle.Bold, null, null),
            new("constant", null, 4, FontStyle.Italic, "#ff0000", null),
            new("constant.numeric", null, 5, FontStyle.NotSet, "#00ff00", null),
            new("constant.numeric.hex", null, 6, FontStyle.Bold, null, null),
            new("constant.numeric.oct", null, 7, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, null, null),
            new("constant.numeric.bin", null, 8, FontStyle.Bold | FontStyle.Strikethrough, null, null),
            new("constant.numeric.dec", null, 9, FontStyle.None, "#0000ff", null),
            new("foo", null, 10, FontStyle.None, "#CFA", null),
        ];

        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Scope, actual[i].Scope);
            Assert.Equal(expected[i].ParentScopes, actual[i].ParentScopes);
            Assert.Equal(expected[i].Index, actual[i].Index);
            Assert.Equal(expected[i].FontStyle, actual[i].FontStyle);
            Assert.Equal(expected[i].Foreground, actual[i].Foreground);
            Assert.Equal(expected[i].Background, actual[i].Background);
        }
    }

    [Fact]
    public void ResolvingStrArrCmpWorks()
    {
        Assert.Equal(0, Theme.CompareParentScopes(null, null));
        Assert.Equal(-1, Theme.CompareParentScopes(null, []));
        Assert.Equal(-1, Theme.CompareParentScopes(null, ["a"]));
        Assert.Equal(1, Theme.CompareParentScopes([], null));
        Assert.Equal(1, Theme.CompareParentScopes(["a"], null));
        Assert.Equal(0, Theme.CompareParentScopes([], []));
        Assert.Equal(-1, Theme.CompareParentScopes([], ["a"]));
        Assert.Equal(1, Theme.CompareParentScopes(["a"], []));
        Assert.Equal(0, Theme.CompareParentScopes(["a"], ["a"]));
        Assert.Equal(1, Theme.CompareParentScopes(["a", "b"], ["a"]));
        Assert.Equal(-1, Theme.CompareParentScopes(["a"], ["a", "b"]));
        Assert.Equal(0, Theme.CompareParentScopes(["a", "b"], ["a", "b"]));
        Assert.Equal(-1, Theme.CompareParentScopes(["a", "b"], ["a", "c"]));
        Assert.Equal(1, Theme.CompareParentScopes(["a", "c"], ["a", "b"]));
    }

    [Fact]
    public void ResolvingAlwaysHasDefaults()
    {
        var theme = Theme.Resolve([]);

        AssertDefaults(theme, FontStyle.None, "#000000", "#FFFFFF");
        AssertRule(theme.Root.MainRule, 0, FontStyle.NotSet, 0, 0);
    }

    public static TheoryData<FontStyle, string?, string?, FontStyle, string, string> IncomingDefaults => new()
    {
        { FontStyle.NotSet, null, null, FontStyle.None, "#000000", "#FFFFFF" },
        { FontStyle.None, null, null, FontStyle.None, "#000000", "#FFFFFF" },
        { FontStyle.Bold, null, null, FontStyle.Bold, "#000000", "#FFFFFF" },
        { FontStyle.NotSet, "#ff0000", null, FontStyle.None, "#FF0000", "#FFFFFF" },
        { FontStyle.NotSet, null, "#ff0000", FontStyle.None, "#000000", "#FF0000" },
    };

    [Theory]
    [MemberData(nameof(IncomingDefaults))]
    public void ResolvingRespectsIncomingDefaults(FontStyle fontStyle, string? foreground, string? background, FontStyle expectedFontStyle, string expectedForeground, string expectedBackground)
    {
        var theme = Theme.Resolve([new(string.Empty, null, -1, fontStyle, foreground, background)]);

        AssertDefaults(theme, expectedFontStyle, expectedForeground, expectedBackground);
        AssertRule(theme.Root.MainRule, 0, FontStyle.NotSet, 0, 0);
    }

    [Fact]
    public void ResolvingCanMergeIncomingDefaults()
    {
        var theme = Theme.Resolve(
        [
            new(string.Empty, null, -1, FontStyle.NotSet, null, "#ff0000"),
            new(string.Empty, null, -1, FontStyle.NotSet, "#00ff00", null),
            new(string.Empty, null, -1, FontStyle.Bold, null, null),
        ]);

        AssertDefaults(theme, FontStyle.Bold, "#00FF00", "#FF0000");
    }

    [Fact]
    public void ResolvingDefaultsAreInherited()
    {
        var theme = Theme.Resolve(
        [
            new(string.Empty, null, -1, FontStyle.NotSet, "#F8F8F2", "#272822"),
            new("var", null, -1, FontStyle.NotSet, "#ff0000", null),
        ]);

        AssertDefaults(theme, FontStyle.None, "#F8F8F2", "#272822");
        AssertRule(theme.Root.MainRule, 0, FontStyle.NotSet, 0, 0);
        AssertRule(theme.Root.Children["var"].MainRule, 1, FontStyle.NotSet, theme.ColorMap.GetId("#ff0000"), 0);
    }

    [Fact]
    public void ResolvingSameRulesGetMerged()
    {
        var theme = Theme.Resolve(
        [
            new(string.Empty, null, -1, FontStyle.NotSet, "#F8F8F2", "#272822"),
            new("var", null, 1, FontStyle.Bold, null, null),
            new("var", null, 0, FontStyle.NotSet, "#ff0000", null),
        ]);

        AssertRule(theme.Root.Children["var"].MainRule, 1, FontStyle.Bold, theme.ColorMap.GetId("#ff0000"), 0);
    }

    [Fact]
    public void ResolvingRulesAreInherited()
    {
        var theme = Theme.Resolve(
        [
            new(string.Empty, null, -1, FontStyle.NotSet, "#F8F8F2", "#272822"),
            new("var", null, -1, FontStyle.Bold, "#ff0000", null),
            new("var.identifier", null, -1, FontStyle.NotSet, "#00ff00", null),
        ]);

        var var = theme.Root.Children["var"];
        AssertRule(var.MainRule, 1, FontStyle.Bold, theme.ColorMap.GetId("#ff0000"), 0);
        AssertRule(var.Children["identifier"].MainRule, 2, FontStyle.Bold, theme.ColorMap.GetId("#00ff00"), 0);
    }

    [Fact]
    public void ResolvingRulesAreInherited2()
    {
        var theme = Theme.Resolve(
        [
            new(string.Empty, null, -1, FontStyle.NotSet, "#F8F8F2", "#272822"),
            new("var", null, -1, FontStyle.Bold, "#ff0000", null),
            new("var.identifier", null, -1, FontStyle.NotSet, "#00ff00", null),
            new("constant", null, 4, FontStyle.Italic, "#100000", null),
            new("constant.numeric", null, 5, FontStyle.NotSet, "#200000", null),
            new("constant.numeric.hex", null, 6, FontStyle.Bold, null, null),
            new("constant.numeric.oct", null, 7, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, null, null),
            new("constant.numeric.dec", null, 8, FontStyle.None, "#300000", null),
        ]);

        var colors = theme.ColorMap;
        var constant = theme.Root.Children["constant"];
        var numeric = constant.Children["numeric"];
        AssertRule(theme.Root.Children["var"].MainRule, 1, FontStyle.Bold, colors.GetId("#ff0000"), 0);
        AssertRule(theme.Root.Children["var"].Children["identifier"].MainRule, 2, FontStyle.Bold, colors.GetId("#00ff00"), 0);
        AssertRule(constant.MainRule, 1, FontStyle.Italic, colors.GetId("#100000"), 0);
        AssertRule(numeric.MainRule, 2, FontStyle.Italic, colors.GetId("#200000"), 0);
        AssertRule(numeric.Children["hex"].MainRule, 3, FontStyle.Bold, colors.GetId("#200000"), 0);
        AssertRule(numeric.Children["oct"].MainRule, 3, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, colors.GetId("#200000"), 0);
        AssertRule(numeric.Children["dec"].MainRule, 3, FontStyle.None, colors.GetId("#300000"), 0);
    }

    [Fact]
    public void ResolvingRulesWithParentScopes()
    {
        var theme = Theme.Resolve(
        [
            new(string.Empty, null, -1, FontStyle.NotSet, "#F8F8F2", "#272822"),
            new("var", null, -1, FontStyle.Bold, "#100000", null),
            new("var.identifier", null, -1, FontStyle.NotSet, "#200000", null),
            new("var", ["source.css"], 1, FontStyle.Italic, "#300000", null),
            new("var", ["source.css"], 2, FontStyle.Underline, null, null),
        ]);

        var colors = theme.ColorMap;
        var var = theme.Root.Children["var"];
        AssertRule(var.MainRule, 1, FontStyle.Bold, colors.GetId("#100000"), 0);
        var withParents = Assert.Single(var.RulesWithParentScopes);
        Assert.Equal(["source.css"], withParents.ParentScopes);
        AssertRule(withParents, 1, FontStyle.Underline, colors.GetId("#300000"), 0);

        var identifier = var.Children["identifier"];
        AssertRule(identifier.MainRule, 2, FontStyle.Bold, colors.GetId("#200000"), 0);
        var inherited = Assert.Single(identifier.RulesWithParentScopes);
        Assert.Equal(["source.css"], inherited.ParentScopes);
        AssertRule(inherited, 1, FontStyle.Underline, colors.GetId("#300000"), 0);
    }

    [Fact]
    public void ResolvingARuleWithChildCombinator()
    {
        var theme = Create(
            Global("#100000"),
            Rule("b a", "#200000"),
            Rule("b > a", "#300000"),
            Rule("c > b > a", "#400000"),
            Rule("a", "#500000"));

        Assert.Equal("#300000", Foreground(theme, "b", "a"));
        Assert.Equal("#200000", Foreground(theme, "b", "c", "a"));
        Assert.Equal("#400000", Foreground(theme, "c", "b", "a"));
        Assert.Equal("#200000", Foreground(theme, "c", "b", "d", "a"));
    }

    [Fact]
    public void ResolvingGivesDeeperScopesHigherSpecificity()
    {
        var theme = Create(
            Global("#100000"),
            Rule("y.z a.b", "#200000"),
            Rule("x y a.b", "#300000"));

        string? Match(params string[] path)
            => theme.Match(path) is { ForegroundId: not 0 } style ? theme.ColorMap.GetColor(style.ForegroundId) : null;

        Assert.Null(Match("x", "a.b"));
        Assert.Null(Match("y", "a.b"));
        Assert.Null(Match("y.z", "a"));
        Assert.Equal("#300000", Match("x", "y", "a.b"));
        Assert.Equal("#200000", Match("x", "y.z", "a.b"));
    }

    [Fact]
    public void ParsingIgnoresRulesWithInvalidColors()
    {
        var actual = Theme.Parse(new RawTheme(
        [
            Global("#cccccc", "#222222"),
            Rule("variable", fontStyle: ""),
            Rule("variable.parameter", "", fontStyle: "italic"),
            Rule("support.other.variable", fontStyle: ""),
            Rule("variable.other", "", fontStyle: "normal"),
            Rule("variable.parameter.function.coffee", "#F9D423", fontStyle: "italic"),
        ]));

        Assert.Equal(
            ["|#cccccc|#222222|NotSet", "variable|||None", "variable.parameter|||Italic", "support.other.variable|||None", "variable.other|||None", "variable.parameter.function.coffee|#F9D423||Italic"],
            actual.Select(r => $"{r.Scope}|{r.Foreground}|{r.Background}|{r.FontStyle}"));
    }

    [Fact]
    public void ParsingAcceptsTrailingCommasInScopeSelectors()
    {
        var actual = Theme.Parse(new RawTheme([Rule("meta.property-list, meta.property-value,", "#FFFFFF")]));

        Assert.Equal(["meta.property-list", "meta.property-value"], actual.Select(r => r.Scope));
    }

    [Fact]
    public void InvalidColorsAreIgnored()
    {
        var theme = Create(Global("#000001", "#000002"), Rule("a", "red"), Rule("b", "#12345"));

        Assert.Equal(0, theme.Match(["a"])!.Value.ForegroundId);
        Assert.Equal(0, theme.Match(["b"])!.Value.ForegroundId);
    }

    private static void AssertDefaults(Theme theme, FontStyle fontStyle, string foreground, string background)
    {
        Assert.Equal(fontStyle, theme.Defaults.FontStyle);
        Assert.Equal(foreground, theme.ColorMap.GetColor(theme.Defaults.ForegroundId));
        Assert.Equal(background, theme.ColorMap.GetColor(theme.Defaults.BackgroundId));
    }

    private static void AssertRule(Theme.ThemeTrieElementRule rule, int scopeDepth, FontStyle fontStyle, int foreground, int background)
    {
        Assert.Equal(scopeDepth, rule.ScopeDepth);
        Assert.Equal((int)fontStyle, rule.FontStyle);
        Assert.Equal(foreground, rule.Foreground);
        Assert.Equal(background, rule.Background);
    }

    private static string? Foreground(Theme theme, params string[] path)
        => theme.Match(path) is { } style ? theme.ColorMap.GetColor(style.ForegroundId) : null;

    private static Theme Create(params RawThemeRule[] rules) => Theme.FromSource(new RawTheme(rules));

    private static RawThemeRule Global(string? foreground = null, string? background = null)
        => new() { Foreground = foreground, Background = background };

    private static RawThemeRule Rule(string scope, string? foreground = null, string? background = null, string? fontStyle = null)
        => new() { Scopes = scope.Trim(',').Split(','), Foreground = foreground, Background = background, FontStyle = fontStyle };

    private static RawThemeRule Rule(string[] scopes, string? foreground = null, string? background = null, string? fontStyle = null)
        => new() { Scopes = scopes, Foreground = foreground, Background = background, FontStyle = fontStyle };

    private static string FontStyleToString(FontStyle fontStyle)
    {
        if (fontStyle == FontStyle.NotSet)
        {
            return "not set";
        }

        var parts = new List<string>();
        if ((fontStyle & FontStyle.Italic) != 0)
        {
            parts.Add("italic");
        }

        if ((fontStyle & FontStyle.Bold) != 0)
        {
            parts.Add("bold");
        }

        if ((fontStyle & FontStyle.Underline) != 0)
        {
            parts.Add("underline");
        }

        if ((fontStyle & FontStyle.Strikethrough) != 0)
        {
            parts.Add("strikethrough");
        }

        return parts.Count == 0 ? "none" : string.Join(' ', parts);
    }
}
