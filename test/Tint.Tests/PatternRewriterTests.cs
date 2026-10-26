using Tint.Grammars;
using Tint.Internal.Regex;
using Tint.Internal.Rules;
using Xunit;

namespace Tint.Tests;

public sealed class PatternRewriterTests
{
    [Theory]
    [InlineData(@"[\xa0-\xF7]", @"[\x{a0}-\x{F7}]")]
    [InlineData(@"[\xABC-\xF77]", @"[\x{ABC}-\x{F77}]")]
    [InlineData(@"\A(?:\xEF\xBB\xBF)", @"\A(?:\x{EF}\x{BB}\x{BF})")]
    [InlineData(@"[a-z_\x7f-\x7fffffff]", @"[a-z_\x{7f}-\x{7ffff}]")]
    [InlineData(@"[a-z_\x{7f}-\x{7ffffff}]", @"[a-z_\x{7f}-\x{7ffffff}]")]
    [InlineData(@"[a-z_\x{7f}-\x{7fffffff}]", @"[a-z_\x{7f}-\x{7ffff}]")]
    [InlineData(@"\x4", @"\x4")]
    [InlineData(@"plain", @"plain")]
    public void HexEscapesGetBracesAsOnigwrapAddsThem(string pattern, string expected)
    {
        Assert.Equal(expected, new RegexSource(pattern).Text);
    }

    [Theory]
    [InlineData(@"a\z", @"a$(?!\n)(?<!\n)")]
    [InlineData(@"\z|b\z", @"$(?!\n)(?<!\n)|b$(?!\n)(?<!\n)")]
    [InlineData(@"a\\z", @"a\\z")]
    public void EndOfStringMeansTheEndOfTheLine(string pattern, string expected)
    {
        Assert.Equal(expected, new RegexSource(pattern).Text);
    }

    [Fact]
    public void AnchorsAreDetectedOutsideEscapedBackslashes()
    {
        Assert.True(new RegexSource(@"\Gfoo").HasAnchorG);
        Assert.False(new RegexSource(@"\Gfoo").HasAnchorA);
        Assert.True(new RegexSource(@"\Afoo").HasAnchorA);
        Assert.False(new RegexSource(@"\\Gfoo").HasAnchor);
    }

    [Fact]
    public void AnchorVariantsDisableTheAnchorsTheyDoNotAllow()
    {
        var source = new RegexSource(@"\A\G\\G");

        Assert.Equal("\\￿\\￿\\\\G", source.GetText(RegexAnchorVariant.A0G0));
        Assert.Equal("\\￿\\G\\\\G", source.GetText(RegexAnchorVariant.A0G1));
        Assert.Equal("\\A\\￿\\\\G", source.GetText(RegexAnchorVariant.A1G0));
        Assert.Equal(@"\A\G\\G", source.GetText(RegexAnchorVariant.A1G1));
    }

    [Fact]
    public void VariantsOfAPatternWithoutAnchorsShareOneCompiledPattern()
    {
        var source = new RegexSource("foo");

        Assert.Same(source.GetCompiled(RegexAnchorVariant.A0G0), source.GetCompiled(RegexAnchorVariant.A1G1));
        Assert.Equal("foo", source.GetText(RegexAnchorVariant.A1G0));
    }

    [Theory]
    [InlineData(@"\1", true)]
    [InlineData(@"</\1>", true)]
    [InlineData(@"\\1", true)]
    [InlineData(@"\w+", false)]
    public void BackReferencesAreDetectedAsVSCodeTextMateDoes(string pattern, bool expected)
    {
        Assert.Equal(expected, new RegexSource(pattern).HasBackReferences);
    }

    [Fact]
    public void ResolvedBackReferencesAreEscaped()
    {
        var line = "<a.b|c(d)>";
        CaptureSpan[] captures = [new(0, line.Length), new(1, 9)];
        var buffer = new CharBuffer();

        PatternRewriter.ResolveBackReferences(@"</\1>\2", line, captures, buffer);

        Assert.Equal(@"</a\.b\|c\(d\)>", buffer.ToString());
    }

    [Fact]
    public void ALoneSurrogateInABackReferenceMatchesAnySurrogate()
    {
        var line = "x\uD800y";
        CaptureSpan[] captures = [new(0, 3), new(1, 2)];
        var buffer = new CharBuffer();

        PatternRewriter.ResolveBackReferences(@"\1", line, captures, buffer);

        Assert.Equal(@"(?:\p{Cs}|\x{FFFD})", buffer.ToString());
    }

    [Theory]
    [InlineData("entity.name.$1", "entity.name.Foo")]
    [InlineData("entity.name.${1:/downcase}", "entity.name.foo")]
    [InlineData("entity.name.${1:/upcase}", "entity.name.FOO")]
    [InlineData("a.$2.b", "a.bar.b")]
    [InlineData("a.$9", "a.$9")]
    [InlineData("a.${1:/other}", "a.${1:/other}")]
    [InlineData("$$1", "$Foo")]
    public void CaptureReferencesInNamesAreReplaced(string name, string expected)
    {
        var line = "Foo ..bar";
        CaptureSpan[] captures = [new(0, 9), new(0, 3), new(4, 9)];
        var buffer = new CharBuffer();

        Assert.True(CaptureNames.HasCaptures(name) || name == expected);
        CaptureNames.Replace(name, line, captures, buffer);

        Assert.Equal(expected, buffer.ToString());
    }

    [Fact]
    public void AnInvalidPatternFailsOnlyWhenASearchReachesIt()
    {
        var registry = new GrammarRegistry();
        registry.Add(Tint.Loading.GrammarLoader.Load("""
            {
              "scopeName": "source.invalid",
              "patterns": [
                { "match": "ok|$", "name": "ok" },
                { "match": "(unclosed", "name": "bad" }
              ]
            }
            """u8));
        var tokenizer = registry.CreateTokenizer("source.invalid");

        var tokens = tokenizer.TokenizeLine("ok", StateStack.Initial, out _);
        Assert.Equal("ok", tokens[0].Scopes.ScopeName);

        var exception = Assert.Throws<TintGrammarException>(() => tokenizer.TokenizeLine("no", StateStack.Initial, out _));
        Assert.Contains("(unclosed", exception.Message);
    }
}
