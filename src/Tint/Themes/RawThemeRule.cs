namespace Tint.Themes;

/// <summary>One token color rule of a theme (<c>tokenColors</c> or <c>settings</c> entry).</summary>
public sealed class RawThemeRule
{
    /// <summary>The descriptive name of the rule.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The scope selectors (each a space-separated scope path), or null for the global rule that sets the defaults.
    /// A comma-separated <c>scope</c> string is split into this list.
    /// </summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>Space-separated font style words (<c>italic</c>, <c>bold</c>, <c>underline</c>, <c>strikethrough</c>), or null when unset.</summary>
    public string? FontStyle { get; init; }

    /// <summary>The foreground color (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c>), or null.</summary>
    public string? Foreground { get; init; }

    /// <summary>The background color, or null.</summary>
    public string? Background { get; init; }
}
