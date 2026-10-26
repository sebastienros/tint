namespace Tint;

/// <summary>A token of a line with its colors resolved against the theme.</summary>
/// <param name="Start">The offset of the first character in the line.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="Color">The foreground color as the theme writes it (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>, upper case).</param>
/// <param name="BackgroundColor">The background color, in the same form.</param>
/// <param name="FontStyle">The font style.</param>
/// <param name="Scopes">The scope stack of the token.</param>
public readonly record struct HighlightedToken(int Start, int Length, string Color, string BackgroundColor, FontStyle FontStyle, AttributedScopeStack Scopes)
{
    /// <summary>The offset just past the last character.</summary>
    public int End => Start + Length;
}
