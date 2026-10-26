namespace Tint.Themes;

/// <summary>A theme as <see cref="Theme.FromSource(IThemeSource)"/> consumes it: loaded from a file, generated or built in code.</summary>
public interface IThemeSource
{
    /// <summary>The display name of the theme, or null.</summary>
    string? Name { get; }

    /// <summary>The theme's rules and colors. Built once and cached by the source.</summary>
    RawTheme GetTheme();
}
