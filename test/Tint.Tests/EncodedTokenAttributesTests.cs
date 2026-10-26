using Tint.Grammars;
using Xunit;

namespace Tint.Tests;

// The StackElementMetadata cases of vscode-textmate's src/tests/grammar.test.ts.
public sealed class EncodedTokenAttributesTests
{
    private const FontStyle UnderlineBold = FontStyle.Underline | FontStyle.Bold;

    [Fact]
    public void SetPacksEveryField()
    {
        var value = EncodedTokenAttributes.Set(0, 1, StandardTokenType.RegEx, false, UnderlineBold, 101, 102);

        AssertEquals(value, 1, StandardTokenType.RegEx, false, UnderlineBold, 101, 102);
    }

    [Fact]
    public void SetCanOverwriteTheLanguageId()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 2, null, false, FontStyle.NotSet, 0, 0);

        AssertEquals(value, 2, StandardTokenType.RegEx, false, UnderlineBold, 101, 102);
    }

    [Fact]
    public void SetCanOverwriteTheTokenType()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 0, StandardTokenType.Comment, false, FontStyle.NotSet, 0, 0);

        AssertEquals(value, 1, StandardTokenType.Comment, false, UnderlineBold, 101, 102);
    }

    [Fact]
    public void SetCanOverwriteTheFontStyle()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 0, null, false, FontStyle.None, 0, 0);

        AssertEquals(value, 1, StandardTokenType.RegEx, false, FontStyle.None, 101, 102);
    }

    [Fact]
    public void SetCanOverwriteStrikethrough()
    {
        var value = EncodedTokenAttributes.Set(0, 1, StandardTokenType.RegEx, false, FontStyle.Strikethrough, 101, 102);
        AssertEquals(value, 1, StandardTokenType.RegEx, false, FontStyle.Strikethrough, 101, 102);

        value = EncodedTokenAttributes.Set(value, 0, null, false, FontStyle.None, 0, 0);
        AssertEquals(value, 1, StandardTokenType.RegEx, false, FontStyle.None, 101, 102);
    }

    [Fact]
    public void SetCanOverwriteTheForeground()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 0, null, false, FontStyle.NotSet, 5, 0);

        AssertEquals(value, 1, StandardTokenType.RegEx, false, UnderlineBold, 5, 102);
    }

    [Fact]
    public void SetCanOverwriteTheBackground()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 0, null, false, FontStyle.NotSet, 0, 7);

        AssertEquals(value, 1, StandardTokenType.RegEx, false, UnderlineBold, 101, 7);
    }

    [Fact]
    public void SetCanOverwriteTheBalancedBracketBit()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 0, null, true, FontStyle.NotSet, 0, 0);
        AssertEquals(value, 1, StandardTokenType.RegEx, true, UnderlineBold, 101, 102);

        value = EncodedTokenAttributes.Set(value, 0, null, false, FontStyle.NotSet, 0, 0);
        AssertEquals(value, 1, StandardTokenType.RegEx, false, UnderlineBold, 101, 102);
    }

    [Fact]
    public void SetWorksAtMaximumValues()
    {
        var value = EncodedTokenAttributes.Set(0, 255, StandardTokenType.RegEx, true, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, 511, 254);

        AssertEquals(value, 255, StandardTokenType.RegEx, true, FontStyle.Bold | FontStyle.Italic | FontStyle.Underline, 511, 254);
    }

    [Fact]
    public void NullTokenTypeAndBracketsKeepTheirValues()
    {
        var value = EncodedTokenAttributes.Set(Initial(), 0, null, null, FontStyle.NotSet, 0, 0);

        Assert.Equal(Initial(), value);
    }

    [Fact]
    public void TheLayoutMatchesVSCode()
    {
        var value = EncodedTokenAttributes.Set(0, 0x12, StandardTokenType.String, true, FontStyle.Italic, 3, 4);

        Assert.Equal(0x12u | (2u << 8) | (1u << 10) | (1u << 11) | (3u << 15) | (4u << 24), value);
    }

    private static uint Initial() => EncodedTokenAttributes.Set(0, 1, StandardTokenType.RegEx, false, UnderlineBold, 101, 102);

    private static void AssertEquals(uint metadata, int languageId, StandardTokenType tokenType, bool containsBalancedBrackets, FontStyle fontStyle, int foreground, int background)
    {
        Assert.Equal(languageId, EncodedTokenAttributes.GetLanguageId(metadata));
        Assert.Equal(tokenType, EncodedTokenAttributes.GetTokenType(metadata));
        Assert.Equal(containsBalancedBrackets, EncodedTokenAttributes.ContainsBalancedBrackets(metadata));
        Assert.Equal(fontStyle, EncodedTokenAttributes.GetFontStyle(metadata));
        Assert.Equal(foreground, EncodedTokenAttributes.GetForeground(metadata));
        Assert.Equal(background, EncodedTokenAttributes.GetBackground(metadata));
    }
}
