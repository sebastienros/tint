namespace Tint.Themes;

/// <summary>The style a theme rule assigns: font style and color ids in the theme's <see cref="ColorMap"/> (0 = not set).</summary>
/// <param name="FontStyle">The font style, or <see cref="Tint.FontStyle.NotSet"/>.</param>
/// <param name="ForegroundId">The foreground color id.</param>
/// <param name="BackgroundId">The background color id.</param>
public readonly record struct StyleAttributes(FontStyle FontStyle, int ForegroundId, int BackgroundId);
