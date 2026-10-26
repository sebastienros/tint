namespace Tint.Rendering;

/// <summary>How <see cref="HtmlRenderer"/> attaches styles to tokens.</summary>
public enum HtmlStyleMode
{
    /// <summary><c>style</c> attributes with the colors, as shiki does.</summary>
    InlineStyles = 0,

    /// <summary>
    /// <c>class</c> attributes (<c>tint-fg-3</c>, <c>tint-i</c>…) styled by <see cref="HtmlRenderer.GetStyleSheet"/>.
    /// One theme only.
    /// </summary>
    CssClasses = 1,
}

/// <summary>Settings of <see cref="HtmlRenderer"/>.</summary>
public sealed class HtmlRendererOptions
{
    /// <summary>The default options.</summary>
    public static HtmlRendererOptions Default { get; } = new();

    /// <summary>The class of the <c>pre</c> element, <c>tint</c> by default.</summary>
    public string PreClass { get; init; } = "tint";

    /// <summary>Inline styles (the default) or CSS classes.</summary>
    public HtmlStyleMode StyleMode { get; init; }

    /// <summary>The prefix of the token classes in <see cref="HtmlStyleMode.CssClasses"/> mode, <c>tint-</c> by default.</summary>
    public string ClassPrefix { get; init; } = "tint-";

    /// <summary>
    /// The prefix of the CSS variables that carry the colors of the additional themes, <c>--tint-</c> by default: a
    /// theme named <c>dark</c> writes <c>--tint-dark</c>, <c>--tint-dark-bg</c>, <c>--tint-dark-font-style</c>…
    /// </summary>
    public string CssVariablePrefix { get; init; } = "--tint-";

    /// <summary>
    /// Whether each line carries its number as a <c>data-line</c> attribute, for a CSS rule such as
    /// <c>.tint .line::before { content: attr(data-line) }</c>. Off by default.
    /// </summary>
    public bool LineNumbers { get; init; }

    /// <summary>The number of the first line when <see cref="LineNumbers"/> is on, 1 by default.</summary>
    public int FirstLineNumber { get; init; } = 1;
}
