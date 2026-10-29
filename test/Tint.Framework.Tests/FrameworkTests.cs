using System.Text;
using Tint.Loading;
using Tint.Rendering;
using Tint.Themes;
using Xunit;

namespace Tint.Framework.Tests;

public sealed class FrameworkTests
{
    [Fact]
    public void Subexpression_calls_compile_on_framework()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes("""
            { "scopeName": "source.test", "patterns": [
              { "match": "(?<word>a)\\g<word>", "name": "keyword.repeat" }
            ] }
            """).AsSpan()));
        var tokenizer = registry.CreateTokenizer("source.test");
        var token = Assert.Single(tokenizer.TokenizeLine("aa".AsSpan(), StateStack.Initial, out _).ToArray());
        Assert.Equal(2, token.Length);
        Assert.Equal("keyword.repeat", token.Scopes.ScopeName);
    }

    [Fact]
    public void Runtime_grammars_preserve_independent_document_states()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes("""
            {
              "scopeName": "source.host",
              "patterns": [{ "include": "source.embedded" }]
            }
            """).AsSpan()));
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes("""
            {
              "scopeName": "source.embedded",
              "patterns": [{ "begin": "\"", "end": "\"", "name": "string.quoted" }]
            }
            """).AsSpan()));
        var tokenizer = registry.CreateTokenizer("source.host");
        tokenizer.TokenizeLine("\"first".AsSpan(), StateStack.Initial, out var first);
        tokenizer.TokenizeLine("plain".AsSpan(), StateStack.Initial, out var second);
        var firstTokens = tokenizer.TokenizeLine("continued\"".AsSpan(), first, out _).ToArray();
        var secondTokens = tokenizer.TokenizeLine("still plain".AsSpan(), second, out _).ToArray();

        Assert.Contains(firstTokens, token => token.Scopes.GetScopeNames().Contains("string.quoted"));
        Assert.Contains(firstTokens, token => token.Scopes.ScopeName == "string.quoted"
            && token.Scopes.GrammarScopeName == "source.embedded");
        Assert.DoesNotContain(secondTokens, token => token.Scopes.GetScopeNames().Contains("string.quoted"));
        Assert.Empty(tokenizer.MissingGrammars);
    }

    [Fact]
    public void Plist_grammar_and_rtl_encoded_tokens_work_on_framework()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes("""
            <plist version="1.0"><dict>
              <key>scopeName</key><string>source.test</string>
              <key>patterns</key><array><dict>
                <key>match</key><string>\d+</string>
                <key>name</key><string>constant.numeric</string>
              </dict></array>
            </dict></plist>
            """).AsSpan()));
        var tokenizer = registry.CreateTokenizer("source.test");
        var tokens = tokenizer.TokenizeLine("42".AsSpan(), StateStack.Initial, out _).ToArray();
        Assert.Contains(tokens, token => token.Scopes.GetScopeNames().Contains("constant.numeric"));
        Assert.NotEmpty(tokenizer.TokenizeLineEncoded("\u05d0 42".AsSpan(), StateStack.Initial, out _).ToArray());
    }

    [Fact]
    public void Multi_theme_html_escapes_text_on_framework()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes("""
            { "scopeName": "source.test", "patterns": [] }
            """).AsSpan()));
        var light = Theme.FromSource(new RawTheme([]));
        var dark = Theme.FromSource(new RawTheme([]));
        var renderer = new HtmlRenderer(light, dark);
        var tokenizers = new[] { registry.CreateTokenizer("source.test", light), registry.CreateTokenizer("source.test", dark) };
        var result = renderer.Render("<hello>&\nworld".AsSpan(), tokenizers);

        Assert.Contains("&lt;hello&gt;&amp;", result);
        Assert.Contains("world", result);
        Assert.Contains("tint-dark", result);
    }

    [Fact]
    public void Identical_scopes_from_different_includes_keep_their_grammar_origins()
    {
        var registry = new GrammarRegistry();
        foreach (var json in new[]
        {
            """{ "scopeName": "source.host", "patterns": [{ "include": "source.first" }, { "include": "source.second" }] }""",
            """{ "scopeName": "source.first", "patterns": [{ "match": "a", "name": "keyword.shared", "captures": { "0": { "name": "capture.shared" } } }] }""",
            """{ "scopeName": "source.second", "patterns": [{ "match": "b", "name": "keyword.shared", "captures": { "0": { "name": "capture.shared" } } }] }"""
        })
        {
            registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes(json).AsSpan()));
        }

        var tokenizer = registry.CreateTokenizer("source.host");
        var tokens = tokenizer.TokenizeLine("ab".AsSpan(), StateStack.Initial, out _).ToArray();
        Assert.Equal("source.first", tokens[0].Scopes.GrammarScopeName);
        Assert.Equal("source.second", tokens[1].Scopes.GrammarScopeName);
        Assert.Equal("source.first", tokens[0].Scopes.Parent!.GrammarScopeName);
        Assert.Equal("source.second", tokens[1].Scopes.Parent!.GrammarScopeName);
        Assert.False(tokens[0].Scopes.Equals(tokens[1].Scopes));
    }

    [Fact]
    public void Synthetic_newline_scopes_survive_removing_the_newline_token()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes("""
            { "scopeName": "source.test", "patterns": [
              { "begin": "^", "end": "\\n", "name": "meta.line",
                "endCaptures": { "0": { "name": "punctuation.linebreak" } } }
            ] }
            """).AsSpan()));
        var tokenizer = registry.CreateTokenizer("source.test");
        var tokens = tokenizer.TokenizeLine("x".AsSpan(), StateStack.Initial, out _).ToArray();
        Assert.Equal(1, Assert.Single(tokens).Length);
        Assert.DoesNotContain("punctuation.linebreak", tokens[0].Scopes.GetScopeNames());
        Assert.Equal("punctuation.linebreak", tokenizer.LineEndScopes.ScopeName);
        Assert.Equal("source.test", tokenizer.LineEndScopes.GrammarScopeName);
    }
}
