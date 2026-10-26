using System.Text;
using Tint.Grammars;
using Tint.Loading;
using Tint.Themes;
using Xunit;

namespace Tint.Tests;

public sealed class RuntimeTests
{
    private static readonly string s_root = Path.GetFullPath(Path.Combine(Fixtures.VSCodeTextMate, "..", "..", "..", ".."));

    [Fact]
    public void CaptureDerivedScopesDoNotGrowTheCachesWithoutBound()
    {
        var registry = new GrammarRegistry();
        registry.Add(Load("""{ "scopeName": "source.words", "patterns": [{ "match": "\\w+", "name": "variable.other.$0" }] }"""));
        var theme = Theme.FromSource(new RawTheme([new RawThemeRule { Scopes = ["variable"], Foreground = "#123456" }]));
        var tokenizer = registry.CreateTokenizer("source.words", theme);

        AttributedScopeStack? root = null;
        var state = StateStack.Initial;
        for (var i = 0; i < 5000; i++)
        {
            var tokens = tokenizer.TokenizeLine($"id{i}", state, out state);
            var token = Assert.Single(tokens.ToArray());
            Assert.Equal($"variable.other.id{i}", token.Scopes.ScopeName);
            Assert.Equal("#123456", theme.ColorMap.GetColor(token.Foreground));
            root = token.Scopes.Parent;
        }

        Assert.NotNull(root);
        Assert.InRange(root.CachedChildCount, 1, AttributedScopeStack.MaxCachedChildren);
        Assert.InRange(theme.CachedMatchCount, 1, Theme.MatchCacheCapacity);
    }

    [Fact]
    public void ABeginRuleWithoutEndStaysOpenToTheEndOfTheLine()
    {
        // VS Code's XML comment rule: the inner rule has a begin and no end, so vscode-textmate gives it "\uFFFF" as
        // its end, which never matches; the second "--" is plain comment text (found by tools/Tint.Fuzz).
        var registry = new GrammarRegistry();
        registry.Add(Load("""
            {
              "scopeName": "text.comments",
              "patterns": [{
                "begin": "<!--", "end": "-->", "name": "comment.block",
                "patterns": [{ "begin": "--(?!>)", "captures": { "0": { "name": "invalid.illegal" } } }]
              }]
            }
            """));
        var tokenizer = registry.CreateTokenizer("text.comments");

        var tokens = tokenizer.TokenizeLine("<!------", StateStack.Initial, out var state).ToArray();

        Assert.Equal(
            [("<!--", "text.comments comment.block"), ("--", "text.comments comment.block invalid.illegal"), ("--", "text.comments comment.block")],
            tokens.Select(t => ("<!------"[t.Start..t.End], t.Scopes.ToString())));
        Assert.Equal(3, state.Depth);
    }

    [Fact]
    public void AStateFromAnotherGrammarIsRejected()
    {
        var registry = new GrammarRegistry();
        registry.Add(Load("""{ "scopeName": "source.a", "patterns": [{ "begin": "\\(", "end": "\\)", "name": "paren" }] }"""));
        registry.Add(Load("""{ "scopeName": "source.b", "patterns": [{ "match": "x", "name": "x" }] }"""));
        registry.CreateTokenizer("source.a").TokenizeLine("(", StateStack.Initial, out var state);

        var exception = Assert.Throws<ArgumentException>(() => registry.CreateTokenizer("source.b").TokenizeLine("x", state, out _));

        Assert.Equal("state", exception.ParamName);
        Assert.Contains("source.a", exception.Message);
    }

    [Fact]
    public void ReplacingAGrammarDropsTheInjectionsItNoLongerDeclares()
    {
        var registry = new GrammarRegistry();
        registry.Add(Load("""{ "scopeName": "source.inject", "injectionSelector": "L:source", "patterns": [] }""", new GrammarInfo { InjectTo = ["source.a", "source.b"] }));
        Assert.Equal(["source.inject"], registry.GetInjections("source.b"));

        registry.Add(Load("""{ "scopeName": "source.inject", "injectionSelector": "L:source", "patterns": [] }""", new GrammarInfo { InjectTo = ["source.a"] }));

        Assert.Equal(["source.inject"], registry.GetInjections("source.a"));
        Assert.Empty(registry.GetInjections("source.b"));
    }

    [Fact]
    public void LanguageIdsBeyondTheEncodedFieldAreRefused()
    {
        var registry = new GrammarRegistry();
        for (var i = 1; i <= 255; i++)
        {
            Assert.Equal(i, registry.GetLanguageId($"language{i}"));
        }

        Assert.Throws<InvalidOperationException>(() => registry.GetLanguageId("one-too-many"));
        Assert.Equal(7, registry.GetLanguageId("language7"));
    }

    [Fact]
    public void ThemesWithMoreColorsThanTheEncodedFieldsAreRefused()
    {
        RawThemeRule Rule(int i, bool background) => new() { Scopes = [$"s{i}"], Foreground = background ? null : $"#{i:x6}", Background = background ? $"#{i:x6}" : null };

        // Two defaults plus 509 foregrounds fill the 9-bit foreground field exactly.
        Theme.FromSource(new RawTheme([.. Enumerable.Range(1, 509).Select(i => Rule(i, false))]));
        Assert.Throws<InvalidOperationException>(() => Theme.FromSource(new RawTheme([.. Enumerable.Range(1, 510).Select(i => Rule(i, false))])));
        Assert.Throws<InvalidOperationException>(() => Theme.FromSource(new RawTheme([.. Enumerable.Range(1, 254).Select(i => Rule(i, true))])));
    }

    [Fact]
    public void VSCodeJsonThemesAlwaysGetADefaultRuleFromTheirType()
    {
        var light = ThemeLoader.Load("""{ "type": "light", "tokenColors": [{ "settings": { "foreground": "#ff0000" } }, { "scope": "a", "settings": { "foreground": "#00ff00" } }] }"""u8).GetTheme();
        var dark = ThemeLoader.Load("""{ "tokenColors": [{ "scope": "a", "settings": { "foreground": "#00ff00" } }] }"""u8).GetTheme();
        var legacy = ThemeLoader.Load("""{ "settings": [{ "settings": { "foreground": "#D4D4D4", "background": "#1E1E1E", "fontStyle": "bold" } }] }"""u8).GetTheme();
        var raw = ThemeLoader.Load("""{ "tokenColors": [{ "settings": { "foreground": "#ff0000" } }] }"""u8, mode: ThemeLoadMode.Raw).GetTheme();

        Assert.Equal(2, light.Rules.Count);
        Assert.Null(light.Rules[0].Scopes);
        Assert.Equal(("#333333", "#FFFFFF"), (light.Rules[0].Foreground, light.Rules[0].Background));
        Assert.Equal(["a"], light.Rules[1].Scopes);

        Assert.Equal(("#BBBBBB", "#1E1E1E"), (dark.Rules[0].Foreground, dark.Rules[0].Background));

        var legacyDefault = Assert.Single(legacy.Rules);
        Assert.Equal(("#D4D4D4", "#1E1E1E", null), (legacyDefault.Foreground, legacyDefault.Background, legacyDefault.FontStyle));
        Assert.Equal("#D4D4D4", legacy.UiColors["editor.foreground"]);

        Assert.Equal("#ff0000", Assert.Single(raw.Rules).Foreground);
    }

    [Fact]
    public void ATimeBudgetStopsALongLineEarlyWithAPrefixOfItsTokens()
    {
        var registry = new GrammarRegistry();
        registry.Add(Load("""{ "scopeName": "source.words", "patterns": [{ "match": "\\w+", "name": "word" }, { "match": ",", "name": "comma" }] }"""));
        var line = string.Join(",", Enumerable.Range(0, 20_000).Select(i => $"w{i}"));
        var tokenizer = registry.CreateTokenizer("source.words");
        var full = tokenizer.TokenizeLine(line, StateStack.Initial, out _).ToArray();
        Assert.False(tokenizer.StoppedEarly);

        tokenizer.TimeBudget = TimeSpan.FromTicks(1);
        var partial = tokenizer.TokenizeLine(line, StateStack.Initial, out _).ToArray();

        Assert.True(tokenizer.StoppedEarly);
        Assert.InRange(partial.Length, 1, full.Length - 1);
        for (var i = 0; i < partial.Length - 1; i++)
        {
            Assert.Equal(full[i], partial[i]);
        }

        Assert.Equal(full[partial.Length - 1].Start, partial[^1].Start);

        tokenizer.TimeBudget = null;
        tokenizer.TokenizeLine(line, StateStack.Initial, out _);
        Assert.False(tokenizer.StoppedEarly);
    }

    [Fact]
    public void ASearchThatRunsOutOfWorkCountsAsNoMatch()
    {
        var registry = new GrammarRegistry();
        registry.Add(Load("""{ "scopeName": "source.slow", "patterns": [{ "match": "^(?:(x)\\1?|xx)+$", "name": "never" }] }"""));
        var tokenizer = registry.CreateTokenizer("source.slow");

        var tokens = tokenizer.TokenizeLine(new string('x', 64) + "!", StateStack.Initial, out _);

        Assert.True(tokenizer.LimitExceededCount > 0);
        Assert.Equal("source.slow", Assert.Single(tokens.ToArray()).Scopes.ScopeName);
    }

    [Fact]
    public void TokenizersOfOneRegistryTokenizeInParallelAlike()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(s_root, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json")));
        var theme = Theme.FromSource(ThemeLoader.Load(Path.Combine(s_root, "themes", "theme-defaults", "dark_plus.json")));
        var lines = File.ReadAllLines(Path.Combine(s_root, "benchmarks", "samples", "medium.cs"));

        var expected = Dump(registry.CreateTokenizer("source.cs", theme), lines);
        var results = new string[8];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            results[i] = Dump(registry.CreateTokenizer("source.cs", theme), lines));

        Assert.All(results, result => Assert.Equal(expected, result));
    }

    private static string Dump(Tokenizer tokenizer, string[] lines)
    {
        var builder = new StringBuilder();
        var state = StateStack.Initial;
        foreach (var line in lines)
        {
            var before = state;
            foreach (var token in tokenizer.TokenizeLine(line, before, out state))
            {
                builder.Append(token.Start).Append(':').Append(token.Length).Append(' ').Append(token.Scopes).Append(' ').Append(token.Scopes.TokenAttributes).Append('\n');
            }

            foreach (var token in tokenizer.TokenizeLineEncoded(line, before, out _))
            {
                builder.Append(token.Start).Append('=').Append(token.Metadata).Append(' ');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static LoadedGrammar Load(string json, GrammarInfo? info = null) => GrammarLoader.Load(Encoding.UTF8.GetBytes(json), DocumentFormat.Json, info);
}
