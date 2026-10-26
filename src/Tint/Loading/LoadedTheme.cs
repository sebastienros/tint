using Tint.Themes;

namespace Tint.Loading;

/// <summary>A theme read from a file by <see cref="ThemeLoader"/>, includes flattened.</summary>
public sealed class LoadedTheme : IThemeSource
{
    private readonly RawTheme _theme;

    internal LoadedTheme(RawTheme theme)
    {
        _theme = theme;
    }

    /// <inheritdoc/>
    public string? Name => _theme.Name;

    /// <inheritdoc/>
    public RawTheme GetTheme() => _theme;
}
