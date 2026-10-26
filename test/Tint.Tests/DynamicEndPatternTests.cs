using Tint.Loading;
using Xunit;

namespace Tint.Tests;

// end and while patterns that refer to captures of their begin match (\3): heredoc delimiters, fence markers.
public sealed class DynamicEndPatternTests
{
    private static readonly string s_grammars = Path.GetFullPath(Path.Combine(Fixtures.VSCodeTextMate, "..", "..", "..", "..", "grammars"));

    [Fact]
    public void AHeredocEndsAtItsOwnDelimiter()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(s_grammars, "shellscript", "syntaxes", "shell-unix-bash.tmLanguage.json")));
        var tokenizer = registry.CreateTokenizer("source.shell");

        var lines = Tokenize(tokenizer, "cat <<'EOF'", "hello END", "EOF", "cat <<'END'", "hello EOF", "END", "echo done");

        Assert.Contains("string.quoted.heredoc.no-indent.EOF", lines[1].Scopes);
        Assert.Contains("punctuation.definition.string.heredoc.delimiter.shell", lines[0].Scopes);
        Assert.DoesNotContain(lines[2].Scopes, s => s.StartsWith("string.quoted.heredoc", StringComparison.Ordinal));
        Assert.Contains("string.quoted.heredoc.no-indent.END", lines[4].Scopes);
        Assert.DoesNotContain(lines[6].Scopes, s => s.StartsWith("string.quoted.heredoc", StringComparison.Ordinal));
        Assert.Contains("EOF", lines[0].State.EndRule);
        Assert.Null(lines[6].State.EndRule);
    }

    [Fact]
    public void TheResolvedEndPatternIsReusedForARepeatedDelimiter()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(s_grammars, "shellscript", "syntaxes", "shell-unix-bash.tmLanguage.json")));
        var tokenizer = registry.CreateTokenizer("source.shell");

        var first = Tokenize(tokenizer, "cat <<'EOF'")[0].State.EndRule;
        var second = Tokenize(tokenizer, "cat <<'EOF'")[0].State.EndRule;

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AFenceClosesOnlyWithItsOwnMarker()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(s_grammars, "markdown-basics", "syntaxes", "markdown.tmLanguage.json")));
        var tokenizer = registry.CreateTokenizer("text.html.markdown");

        var lines = Tokenize(tokenizer, "````", "```", "inside", "````", "# Title");

        Assert.Contains("markup.fenced_code.block.markdown", lines[1].Scopes);
        Assert.Contains("markup.fenced_code.block.markdown", lines[2].Scopes);
        Assert.Contains("markup.fenced_code.block.markdown", lines[3].Scopes);
        Assert.DoesNotContain("markup.fenced_code.block.markdown", lines[4].Scopes);
        Assert.Contains("markup.heading.markdown", lines[4].Scopes);
    }

    [Fact]
    public void FencesOfUnregisteredLanguagesReportTheMissingGrammarAndDropTheEmbeddedRule()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(s_grammars, "markdown-basics", "syntaxes", "markdown.tmLanguage.json")));
        var tokenizer = registry.CreateTokenizer("text.html.markdown");

        var lines = Tokenize(tokenizer, "```csharp", "var x = 1;", "```", "text");

        // The embedded begin/while rule only includes source.cs: with it missing, vscode-textmate drops the rule.
        Assert.Contains("source.cs", tokenizer.MissingGrammars);
        Assert.Contains("markup.fenced_code.block.markdown", lines[1].Scopes);
        Assert.DoesNotContain("meta.embedded.block.csharp", lines[1].Scopes);
        Assert.DoesNotContain("markup.fenced_code.block.markdown", lines[3].Scopes);
    }

    private static List<(HashSet<string> Scopes, StateStack State)> Tokenize(Tokenizer tokenizer, params string[] lines)
    {
        var result = new List<(HashSet<string>, StateStack)>();
        var state = StateStack.Initial;
        foreach (var line in lines)
        {
            var scopes = new HashSet<string>();
            foreach (var token in tokenizer.TokenizeLine(line, state, out state))
            {
                scopes.UnionWith(token.Scopes.GetScopeNames());
            }

            result.Add((scopes, state));
        }

        return result;
    }
}
