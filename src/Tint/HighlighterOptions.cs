using Tint.Rendering;

namespace Tint;

/// <summary>Settings of a <see cref="Highlighter"/>.</summary>
public sealed class HighlighterOptions
{
    /// <summary>The default options.</summary>
    public static HighlighterOptions Default { get; } = new();

    /// <summary>The HTML settings, or null for <see cref="HtmlRendererOptions.Default"/>.</summary>
    public HtmlRendererOptions? Html { get; init; }

    /// <summary>The terminal settings, or null for <see cref="AnsiRendererOptions.Default"/>.</summary>
    public AnsiRendererOptions? Ansi { get; init; }

    /// <summary>The tokenizer settings, or null for the registry's <see cref="GrammarRegistry.TokenizerOptions"/>.</summary>
    public TokenizerOptions? Tokenizer { get; init; }
}
