namespace Tint.Rendering;

/// <summary>Settings of <see cref="AnsiRenderer"/>.</summary>
public sealed class AnsiRendererOptions
{
    /// <summary>The default options: foreground colors and font styles, no background.</summary>
    public static AnsiRendererOptions Default { get; } = new();

    /// <summary>
    /// Whether tokens also carry the theme's background color, the default background included, so lines are painted
    /// with the theme's background. Off by default: the terminal's background shows.
    /// </summary>
    public bool Background { get; init; }
}
