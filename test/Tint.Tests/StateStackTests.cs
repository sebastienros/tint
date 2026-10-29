using System.Text;
using Tint.Loading;
using Xunit;

namespace Tint.Tests;

public sealed class StateStackTests
{
    private const string Grammar = """
        {
          "scopeName": "source.test",
          "patterns": [
            { "begin": "\\(", "end": "\\)", "name": "meta.paren", "patterns": [{ "include": "$self" }] },
            { "begin": "<(\\w+)>", "end": "</\\1>", "name": "meta.tag" },
            { "match": "\\w+", "name": "word" }
          ]
        }
        """;

    [Fact]
    public void InitialIsOnlyEqualToItself()
    {
        var state = Run(new GrammarRegistry(), "x");

        Assert.Equal(StateStack.Initial, StateStack.Initial);
        Assert.NotEqual(StateStack.Initial, state);
        Assert.Equal(0, StateStack.Initial.Depth);
    }

    [Fact]
    public void StatesFromSeparateRunsAreEqualAndHashAlike()
    {
        var registry = CreateRegistry();
        var a = Run(registry, "a ( b", "( c");
        var b = Run(registry, "x ( y", "( z");

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(3, a.Depth);
    }

    [Fact]
    public void StatesFromSeparateTokenizersAreEqual()
    {
        var registry = CreateRegistry();
        var a = Run(registry.CreateTokenizer("source.test"), "( a");
        var b = Run(registry.CreateTokenizer("source.test"), "( b");

        Assert.Equal(a, b);
    }

    [Fact]
    public void StatesWithDifferentOpenRulesDiffer()
    {
        var registry = CreateRegistry();

        Assert.NotEqual(Run(registry, "( a"), Run(registry, "( ( a"));
        Assert.NotEqual(Run(registry, "( a"), Run(registry, "<b> a"));
    }

    [Fact]
    public void StatesWithDifferentResolvedEndPatternsDiffer()
    {
        var registry = CreateRegistry();
        var a = Run(registry, "<b> a");
        var b = Run(registry, "<i> a");

        Assert.NotEqual(a, b);
        Assert.Equal("</b>", a.EndRule);
        Assert.Equal(a, Run(registry, "<b> c"));
    }

    [Fact]
    public void ScopesAreTheOpenScopesTheNextLineInherits()
    {
        var tokenizer = CreateRegistry().CreateTokenizer("source.test");

        Assert.Null(StateStack.Initial.Scopes);

        tokenizer.TokenizeLine("( a", StateStack.Initial, out var open);
        Assert.Equal("source.test meta.paren", open.Scopes!.ToString());
        var continued = tokenizer.TokenizeLine("b", open, out _)[0].Scopes;
        Assert.Equal(open.Scopes, continued.Parent);
    }

    [Fact]
    public void ScopesTellAScopeOpenedAgainFromOneThatContinues()
    {
        var tokenizer = CreateRegistry().CreateTokenizer("source.test");
        tokenizer.TokenizeLine("( a", StateStack.Initial, out var open);
        tokenizer.TokenizeLine("( a )", StateStack.Initial, out var closed);

        // The next line starts with an equal paren scope either way; only the state says whether it is the same one.
        var reopened = tokenizer.TokenizeLine("( b", closed, out _)[0].Scopes;
        Assert.Equal(open.Scopes, reopened);
        Assert.Equal("source.test", closed.Scopes!.ToString());
    }

    [Fact]
    public void ReturnedStatesHoldNoLinePositionsSoStartingALineDoesNotWriteThem()
    {
        var tokenizer = CreateRegistry().CreateTokenizer("source.test");
        tokenizer.TokenizeLine("a ( <b> ( c", StateStack.Initial, out var state);
        AssertNoLinePositions(state);

        // Two lines tokenized from the same kept state see the same input and leave it as it was.
        var first = tokenizer.TokenizeLine("d ) e", state, out var firstNext).ToArray();
        var second = tokenizer.TokenizeLine("d ) e", state, out var secondNext).ToArray();
        Assert.Equal(first, second);
        Assert.Equal(firstNext, secondNext);
        AssertNoLinePositions(state);
        AssertNoLinePositions(firstNext);
    }

    [Fact]
    public void ClosedRulesReturnToTheRootState()
    {
        var registry = CreateRegistry();

        Assert.Equal(Run(registry, "a"), Run(registry, "( a )"));
    }

    private static void AssertNoLinePositions(StateStack state)
    {
        for (var node = state; node is not null; node = node.Parent)
        {
            Assert.Equal(-1, node.EnterPos);
            Assert.Equal(-1, node.AnchorPos);
        }
    }

    private static GrammarRegistry CreateRegistry()
    {
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes(Grammar)));
        return registry;
    }

    private static StateStack Run(GrammarRegistry registry, params string[] lines)
    {
        if (!registry.TryGet("source.test", out _))
        {
            registry.Add(GrammarLoader.Load(Encoding.UTF8.GetBytes(Grammar)));
        }

        return Run(registry.CreateTokenizer("source.test"), lines);
    }

    private static StateStack Run(Tokenizer tokenizer, params string[] lines)
    {
        var state = StateStack.Initial;
        foreach (var line in lines)
        {
            tokenizer.TokenizeLine(line, state, out state);
        }

        return state;
    }
}
