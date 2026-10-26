using Tint.Internal.Selectors;
using Xunit;

namespace Tint.Tests;

// vscode-textmate's src/tests/matcher.test.ts, with its exact-equality name matcher.
public sealed class ScopeSelectorTests
{
    public static TheoryData<string, string[], bool> MatcherCases => new()
    {
        { "foo", ["foo"], true },
        { "foo", ["bar"], false },
        { "- foo", ["foo"], false },
        { "- foo", ["bar"], true },
        { "- - foo", ["bar"], false },
        { "bar foo", ["foo"], false },
        { "bar foo", ["bar"], false },
        { "bar foo", ["bar", "foo"], true },
        { "bar - foo", ["bar"], true },
        { "bar - foo", ["foo", "bar"], false },
        { "bar - foo", ["foo"], false },
        { "bar, foo", ["foo"], true },
        { "bar, foo", ["bar"], true },
        { "bar, foo", ["bar", "foo"], true },
        { "bar, -foo", ["bar", "foo"], true },
        { "bar, -foo", ["yo"], true },
        { "bar, -foo", ["foo"], false },
        { "(foo)", ["foo"], true },
        { "(foo - bar)", ["foo"], true },
        { "(foo - bar)", ["foo", "bar"], false },
        { "foo bar - (yo man)", ["foo", "bar"], true },
        { "foo bar - (yo man)", ["foo", "bar", "yo"], true },
        { "foo bar - (yo man)", ["foo", "bar", "yo", "man"], false },
        { "foo bar - (yo | man)", ["foo", "bar", "yo", "man"], false },
        { "foo bar - (yo | man)", ["foo", "bar", "yo"], false },
        { "R:text.html - (comment.block, text.html source)", ["text.html", "bar", "source"], false },
        { "text.html.php - (meta.embedded | meta.tag), L:text.html.php meta.tag, L:source.js.embedded.html", ["text.html.php", "bar", "source.js"], true },
    };

    [Theory]
    [MemberData(nameof(MatcherCases))]
    public void SelectorMatchesLikeVSCodeTextMate(string expression, string[] input, bool expected)
    {
        var matchers = ScopeSelector.CreateMatchers(expression, ExactNameMatcher);

        Assert.Equal(expected, matchers.Any(m => m.Matcher.Matches(input)));
    }

    [Fact]
    public void PriorityPrefixesSetInjectionPriorities()
    {
        var matchers = ScopeSelector.CreateMatchers("L:source.a, R:source.b, source.c", ScopeSelector.ScopeNameMatcher);

        Assert.Equal([-1, 1, 0], matchers.Select(m => m.Priority));
    }

    [Fact]
    public void ScopeNamesMatchByDotPrefix()
    {
        var matcher = ScopeSelector.CreateMatchers("meta.tag string", ScopeSelector.ScopeNameMatcher).Single().Matcher;

        Assert.True(matcher.Matches(["text.html", "meta.tag.inline.html", "string.quoted.double.html"]));
        Assert.False(matcher.Matches(["text.html", "meta.tags", "string.quoted"]));
        Assert.False(matcher.Matches(["string.quoted", "meta.tag"]));
    }

    private static bool ExactNameMatcher(string[] identifiers, ReadOnlySpan<string> scopes)
    {
        var lastIndex = 0;
        foreach (var identifier in identifiers)
        {
            var found = false;
            for (var i = lastIndex; i < scopes.Length; i++)
            {
                if (scopes[i] == identifier)
                {
                    lastIndex = i + 1;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
