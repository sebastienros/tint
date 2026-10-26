using System.Text;
using Tint.Grammars;
using Tint.Loading;
using Xunit;

namespace Tint.Tests;

public sealed class LoaderTests
{
    [Fact]
    public void JsonGrammarsMayHaveCommentsAndTrailingCommas()
    {
        var grammar = GrammarLoader.Load("""
            // A grammar with comments.
            {
              "scopeName": "source.test", /* inline */
              "name": "Test",
              "patterns": [
                { "include": "#word", },
              ],
              "repository": {
                "word": { "match": "\\w+", "name": "word", },
              },
            }
            """u8).GetGrammar();

        Assert.Equal("source.test", grammar.ScopeName);
        Assert.Equal("Test", grammar.Name);
        Assert.Equal(IncludeReference.Relative("word"), grammar.Patterns[0].Include);
        Assert.Equal(@"\w+", grammar.Repository["word"].Match!.Text);
    }

    [Fact]
    public void AByteOrderMarkIsSkipped()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat("""{ "scopeName": "source.bom", "patterns": [] }"""u8.ToArray()).ToArray();

        Assert.Equal("source.bom", GrammarLoader.Load(bytes).ScopeName);
    }

    [Fact]
    public void PlistGrammarsAreRead()
    {
        var grammar = GrammarLoader.Load("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>scopeName</key>
              <string>source.plist-test</string>
              <key>patterns</key>
              <array>
                <dict>
                  <key>begin</key>
                  <string>&lt;(\w+)&gt;</string>
                  <key>end</key>
                  <string>&lt;/\1&gt;</string>
                  <key>applyEndPatternLast</key>
                  <integer>1</integer>
                  <key>beginCaptures</key>
                  <dict>
                    <key>1</key>
                    <dict><key>name</key><string>entity.name.tag</string></dict>
                  </dict>
                </dict>
                <dict>
                  <key>match</key>
                  <string><![CDATA[a<b]]></string>
                  <key>name</key>
                  <string>cdata</string>
                </dict>
              </array>
              <key>injectionSelector</key>
              <string>L:source.x</string>
            </dict>
            </plist>
            """u8).GetGrammar();

        Assert.Equal("source.plist-test", grammar.ScopeName);
        Assert.Equal("L:source.x", grammar.InjectionSelector);
        var tag = grammar.Patterns[0];
        Assert.Equal(@"<(\w+)>", tag.Begin!.Text);
        Assert.True(tag.End!.HasBackReferences);
        Assert.True(tag.ApplyEndPatternLast);
        Assert.Equal(2, tag.BeginCaptures!.Count);
        Assert.Null(tag.BeginCaptures[0]);
        Assert.Equal("entity.name.tag", tag.BeginCaptures[1]!.Name);
        Assert.Equal("a<b", grammar.Patterns[1].Match!.Text);
    }

    [Fact]
    public void ValuesVSCodeTextMateTreatsAsAbsentAreNull()
    {
        var rule = GrammarLoader.Load("""
            {
              "scopeName": "source.test",
              "patterns": [
                { "name": "", "match": "", "begin": "", "while": "", "include": "", "applyEndPatternLast": 0 }
              ]
            }
            """u8).GetGrammar().Patterns[0];

        Assert.Null(rule.Name);
        Assert.Null(rule.Match);
        Assert.Null(rule.While);
        Assert.Null(rule.Include);
        Assert.NotNull(rule.Begin);
        Assert.Equal(string.Empty, rule.Begin!.Text);
        Assert.False(rule.ApplyEndPatternLast);
    }

    [Fact]
    public void CapturesAreDenseUpToTheLargestNumericKey()
    {
        var captures = GrammarLoader.Load("""
            {
              "scopeName": "source.test",
              "patterns": [
                { "match": "(a)(b)(c)", "captures": { "3": { "name": "c" }, "1": { "name": "a" }, "x": { "name": "ignored" } } }
              ]
            }
            """u8).GetGrammar().Patterns[0].Captures!;

        Assert.Equal(4, captures.Count);
        Assert.Null(captures[0]);
        Assert.Equal("a", captures[1]!.Name);
        Assert.Null(captures[2]);
        Assert.Equal("c", captures[3]!.Name);
    }

    [Theory]
    [InlineData("$self", IncludeReferenceKind.Self, null, null)]
    [InlineData("$base", IncludeReferenceKind.Base, null, null)]
    [InlineData("#rule", IncludeReferenceKind.Relative, null, "rule")]
    [InlineData("source.js", IncludeReferenceKind.TopLevel, "source.js", null)]
    [InlineData("source.js#rule", IncludeReferenceKind.TopLevelRepository, "source.js", "rule")]
    public void IncludesAreParsedOnce(string include, IncludeReferenceKind kind, string? scopeName, string? ruleName)
    {
        var reference = IncludeReference.Parse(include);

        Assert.Equal(kind, reference.Kind);
        Assert.Equal(scopeName, reference.ScopeName);
        Assert.Equal(ruleName, reference.RuleName);
        Assert.Equal(include, reference.ToString());
    }

    [Fact]
    public void DependenciesListTheExternalScopesAGrammarIncludes()
    {
        var grammar = GrammarLoader.Load("""
            {
              "scopeName": "text.test",
              "patterns": [{ "include": "source.js" }, { "include": "text.test#local" }, { "include": "#local" }],
              "repository": {
                "local": { "begin": "<style>", "end": "</style>", "patterns": [{ "include": "source.css#rules" }] }
              }
            }
            """u8);

        Assert.Equal(["source.js", "source.css"], grammar.Info.Dependencies);
    }

    [Fact]
    public void ThemeIncludesAreFlattenedParentFirst()
    {
        var files = new Dictionary<string, string>
        {
            ["base.json"] = """{ "name": "Base", "type": "dark", "colors": { "editor.background": "#000000", "editor.foreground": "#ffffff" }, "tokenColors": [{ "scope": "comment", "settings": { "foreground": "#111111" } }] }""",
            ["sub/legacy.tmTheme"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><dict><key>settings</key><array>
                  <dict><key>scope</key><string>string, ,constant,</string><key>settings</key><dict><key>foreground</key><string>#222222</string></dict></dict>
                </array></dict></plist>
                """,
        };

        var theme = ThemeLoader.Load(
            """
            {
              "name": "Child",
              "include": "./base.json",
              "colors": { "editor.background": "#101010" },
              "tokenColors": "./sub/legacy.tmTheme",
              "settings": [{ "scope": ["keyword"], "settings": { "fontStyle": "bold" } }, { "scope": "no-settings" }]
            }
            """u8,
            DocumentFormat.Json,
            path => files.TryGetValue(path, out var text) ? new MemoryStream(Encoding.UTF8.GetBytes(text)) : null).GetTheme();

        Assert.Equal("Child", theme.Name);
        Assert.Equal("dark", theme.Type);
        Assert.Equal("#101010", theme.UiColors["editor.background"]);
        Assert.Equal("#ffffff", theme.UiColors["editor.foreground"]);

        // The editor colors become the default rule, as VS Code builds it.
        Assert.Equal(4, theme.Rules.Count);
        Assert.Null(theme.Rules[0].Scopes);
        Assert.Equal("#ffffff", theme.Rules[0].Foreground);
        Assert.Equal("#101010", theme.Rules[0].Background);
        Assert.Equal(["comment"], theme.Rules[1].Scopes);
        Assert.Equal(["string", " ", "constant"], theme.Rules[2].Scopes);
        Assert.Equal("#222222", theme.Rules[2].Foreground);
        Assert.Equal("bold", theme.Rules[3].FontStyle);
    }

    [Fact]
    public void BundledThemesLoadWithTheirIncludes()
    {
        var path = Path.GetFullPath(Path.Combine(Fixtures.VSCodeTextMate, "..", "..", "..", "..", "themes", "theme-defaults", "dark_plus.json"));
        var source = ThemeLoader.Load(path);
        var theme = Theme.FromSource(source);

        Assert.Equal("Dark+", theme.Name);
        Assert.Equal("#D4D4D4", theme.ColorMap.GetColor(theme.Defaults.ForegroundId));
        Assert.True(theme.UiColors.ContainsKey("editor.background"));
        Assert.Equal("#6A9955", theme.ColorMap.GetColor(theme.Match(["source.cs", "comment.line.double-slash.cs"])!.Value.ForegroundId));
    }

    [Fact]
    public void TmThemesAreRead()
    {
        var theme = Theme.FromSource(ThemeLoader.Load(Path.Combine(Fixtures.VSCodeTextMate, "themes", "Monokai.tmTheme")));

        Assert.Equal("Monokai", theme.Name);
        Assert.Equal("#F8F8F2", theme.ColorMap.GetColor(theme.Defaults.ForegroundId));
        Assert.Equal("#272822", theme.ColorMap.GetColor(theme.Defaults.BackgroundId));
    }
}
