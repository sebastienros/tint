using Tint.Grammars;

namespace Tint;

/// <summary>A token of a line: its range and the scopes that apply to it.</summary>
/// <param name="Start">The offset of the first character in the line.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="Scopes">The scope stack of the token, root first when materialized.</param>
public readonly record struct Token(int Start, int Length, AttributedScopeStack Scopes)
{
    /// <summary>The offset just past the last character.</summary>
    public int End => Start + Length;

    /// <summary>The foreground color id in the theme's <see cref="Themes.ColorMap"/>.</summary>
    public int Foreground => EncodedTokenAttributes.GetForeground(Scopes.TokenAttributes);

    /// <summary>The background color id in the theme's <see cref="Themes.ColorMap"/>.</summary>
    public int Background => EncodedTokenAttributes.GetBackground(Scopes.TokenAttributes);

    /// <summary>The font style.</summary>
    public FontStyle FontStyle => EncodedTokenAttributes.GetFontStyle(Scopes.TokenAttributes);
}

/// <summary>
/// A token in vscode-textmate's binary form (<c>tokenizeLine2</c>): where it starts and its metadata. It ends where
/// the next token starts, or at the end of the line.
/// </summary>
/// <param name="Start">The offset of the first character in the line.</param>
/// <param name="Metadata">The attributes, packed as <see cref="EncodedTokenAttributes"/> describes.</param>
public readonly record struct EncodedToken(int Start, uint Metadata)
{
    /// <summary>The foreground color id in the theme's <see cref="Themes.ColorMap"/>.</summary>
    public int Foreground => EncodedTokenAttributes.GetForeground(Metadata);

    /// <summary>The background color id in the theme's <see cref="Themes.ColorMap"/>.</summary>
    public int Background => EncodedTokenAttributes.GetBackground(Metadata);

    /// <summary>The font style.</summary>
    public FontStyle FontStyle => EncodedTokenAttributes.GetFontStyle(Metadata);

    /// <summary>The embedded language id.</summary>
    public int LanguageId => EncodedTokenAttributes.GetLanguageId(Metadata);

    /// <summary>The standard token type.</summary>
    public StandardTokenType TokenType => EncodedTokenAttributes.GetTokenType(Metadata);
}
