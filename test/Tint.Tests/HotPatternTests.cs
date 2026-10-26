using Tint.Loading;
using Xunit;

namespace Tint.Tests;

// Tier 2 (docs/design.md section 5.5): swapping interpreted patterns for runtime-compiled ones changes nothing but speed.
public sealed class HotPatternTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(256)]
    public void CompiledPatternsProduceTheInterpretedTokens(int threshold)
    {
        var lines = File.ReadAllLines(Path.Combine(Fixtures.RepositoryRoot, "benchmarks", "samples", "medium.cs"));
        var expected = Tokenize(new GrammarRegistry(), TokenizerOptions.Default, lines, out var interpreted);
        var actual = Tokenize(new GrammarRegistry(), new TokenizerOptions { CompileHotPatterns = true, HotPatternThreshold = threshold }, lines, out var compiled);

        Assert.Equal(expected, actual);
        Assert.Equal(0, interpreted.CompiledPatternCount);
        Assert.True(compiled.CompiledPatternCount > 0, "No pattern was runtime-compiled.");
    }

    [Fact]
    public void CompiledPatternsAreSharedWithLaterTokenizers()
    {
        var registry = CreateRegistry();
        var options = new TokenizerOptions { CompileHotPatterns = true, HotPatternThreshold = 0 };
        var first = registry.CreateTokenizer("source.cs", null, options);
        first.TokenizeLine("class A { int x = 1; }", StateStack.Initial, out _);

        // A later tokenizer with a high threshold picks up the patterns compiled already.
        var second = registry.CreateTokenizer("source.cs", null, new TokenizerOptions { CompileHotPatterns = true, HotPatternThreshold = 1_000_000 });
        second.TokenizeLine("class A { int x = 1; }", StateStack.Initial, out _);
        Assert.True(second.CompiledPatternCount > 0);

        // A tokenizer without the option keeps the interpreter.
        var third = registry.CreateTokenizer("source.cs");
        third.TokenizeLine("class A { int x = 1; }", StateStack.Initial, out _);
        Assert.Equal(0, third.CompiledPatternCount);
    }

    [Fact]
    public void RegistryOptionsApplyToTokenizersAndHighlighters()
    {
        var registry = CreateRegistry();
        var options = new TokenizerOptions { CompileHotPatterns = true, HotPatternThreshold = 0 };
        registry.TokenizerOptions = options;
        Assert.Same(options, registry.CreateTokenizer("source.cs").Options);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenizerOptions { HotPatternThreshold = -1 });
    }

    private static List<string> Tokenize(GrammarRegistry registry, TokenizerOptions options, string[] lines, out Tokenizer tokenizer)
    {
        registry.Add(GrammarLoader.Load(Path.Combine(Fixtures.RepositoryRoot, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json")));
        tokenizer = registry.CreateTokenizer("source.cs", null, options);
        var result = new List<string>();
        var state = StateStack.Initial;
        foreach (var line in lines)
        {
            foreach (var token in tokenizer.TokenizeLine(line, state, out state))
            {
                result.Add($"{token.Start}:{token.Length} {token.Scopes}");
            }
        }

        return result;
    }

    private static GrammarRegistry CreateRegistry()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(Fixtures.RepositoryRoot, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json")));
        return registry;
    }
}
