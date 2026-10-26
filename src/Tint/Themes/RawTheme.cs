using System.Collections.Frozen;

namespace Tint.Themes;

/// <summary>
/// A theme as written: token color rules in declaration order (included themes first) and UI colors.
/// </summary>
public sealed class RawTheme : IThemeSource
{
    /// <summary>Creates a theme.</summary>
    /// <param name="rules">The token color rules, in declaration order.</param>
    /// <param name="name">The display name, or null.</param>
    /// <param name="type">The theme kind (<c>dark</c>, <c>light</c>, <c>hc</c>, ...), or null.</param>
    /// <param name="uiColors">Workbench colors such as <c>editor.background</c>, or null for none.</param>
    public RawTheme(IReadOnlyList<RawThemeRule> rules, string? name = null, string? type = null, IReadOnlyDictionary<string, string>? uiColors = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules = rules;
        Name = name;
        Type = type;
        UiColors = uiColors ?? FrozenDictionary<string, string>.Empty;
    }

    /// <summary>The display name.</summary>
    public string? Name { get; }

    /// <summary>The theme kind (<c>dark</c>, <c>light</c>, <c>hc</c>, ...).</summary>
    public string? Type { get; }

    /// <summary>The token color rules, in declaration order.</summary>
    public IReadOnlyList<RawThemeRule> Rules { get; }

    /// <summary>Workbench colors, for example <c>editor.background</c>.</summary>
    public IReadOnlyDictionary<string, string> UiColors { get; }

    /// <inheritdoc/>
    public RawTheme GetTheme() => this;
}
