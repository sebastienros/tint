using Tint.Grammars;

namespace Tint;

/// <summary>
/// Reads and writes token metadata packed in 32 bits with vscode-textmate's layout: language id (bits 0-7), standard
/// token type (8-9), balanced brackets (10), font style (11-14), foreground color id (15-23), background color id
/// (24-31).
/// </summary>
public static class EncodedTokenAttributes
{
    private const uint LanguageIdMask = 0b0000_0000_0000_0000_0000_0000_1111_1111;
    private const uint TokenTypeMask = 0b0000_0000_0000_0000_0000_0011_0000_0000;
    private const uint BalancedBracketsMask = 0b0000_0000_0000_0000_0000_0100_0000_0000;
    private const uint FontStyleMask = 0b0000_0000_0000_0000_0111_1000_0000_0000;
    private const uint ForegroundMask = 0b0000_0000_1111_1111_1000_0000_0000_0000;
    private const uint BackgroundMask = 0b1111_1111_0000_0000_0000_0000_0000_0000;

    private const int LanguageIdOffset = 0;
    private const int TokenTypeOffset = 8;
    private const int BalancedBracketsOffset = 10;
    private const int FontStyleOffset = 11;
    private const int ForegroundOffset = 15;
    private const int BackgroundOffset = 24;

    /// <summary>The embedded language id (0 when none).</summary>
    public static int GetLanguageId(uint metadata) => (int)((metadata & LanguageIdMask) >> LanguageIdOffset);

    /// <summary>The standard token type.</summary>
    public static StandardTokenType GetTokenType(uint metadata) => (StandardTokenType)((metadata & TokenTypeMask) >> TokenTypeOffset);

    /// <summary>Whether brackets in the token take part in bracket matching.</summary>
    public static bool ContainsBalancedBrackets(uint metadata) => (metadata & BalancedBracketsMask) != 0;

    /// <summary>The font style flags.</summary>
    public static FontStyle GetFontStyle(uint metadata) => (FontStyle)((metadata & FontStyleMask) >> FontStyleOffset);

    /// <summary>The foreground color id in the theme's <see cref="Themes.ColorMap"/>.</summary>
    public static int GetForeground(uint metadata) => (int)((metadata & ForegroundMask) >> ForegroundOffset);

    /// <summary>The background color id in the theme's <see cref="Themes.ColorMap"/>.</summary>
    public static int GetBackground(uint metadata) => (int)((metadata & BackgroundMask) >> BackgroundOffset);

    /// <summary>
    /// Updates fields of <paramref name="metadata"/>. A language id of 0, a null token type or bracket flag,
    /// <see cref="FontStyle.NotSet"/> and a color id of 0 leave the field as it is.
    /// </summary>
    public static uint Set(uint metadata, int languageId, StandardTokenType? tokenType, bool? containsBalancedBrackets, FontStyle fontStyle, int foreground, int background)
    {
        var language = languageId != 0 ? (uint)languageId : (metadata & LanguageIdMask) >> LanguageIdOffset;
        var type = tokenType is { } t ? (uint)t : (metadata & TokenTypeMask) >> TokenTypeOffset;
        var brackets = containsBalancedBrackets is { } b ? (b ? 1u : 0u) : (metadata & BalancedBracketsMask) >> BalancedBracketsOffset;
        var style = fontStyle != FontStyle.NotSet ? (uint)fontStyle : (metadata & FontStyleMask) >> FontStyleOffset;
        var fg = foreground != 0 ? (uint)foreground : (metadata & ForegroundMask) >> ForegroundOffset;
        var bg = background != 0 ? (uint)background : (metadata & BackgroundMask) >> BackgroundOffset;

        return ((language << LanguageIdOffset) & LanguageIdMask)
            | ((type << TokenTypeOffset) & TokenTypeMask)
            | ((brackets << BalancedBracketsOffset) & BalancedBracketsMask)
            | ((style << FontStyleOffset) & FontStyleMask)
            | ((fg << ForegroundOffset) & ForegroundMask)
            | ((bg << BackgroundOffset) & BackgroundMask);
    }
}
