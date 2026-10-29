using System.Runtime.CompilerServices;

namespace Tint.Rendering;

// Per-theme render state for at most MaxThemes themes, kept on the stack so a render allocates nothing per theme.
internal static class ThemeSet
{
    public const int MaxThemes = 8;

    // Checks the tokenizers against the renderer's themes: one per theme, in order, over the same grammar.
    public static void Validate(ReadOnlySpan<Tokenizer> tokenizers, ThemePalette[] palettes, string parameterName)
    {
        if (tokenizers.Length != palettes.Length)
        {
            throw new ArgumentException($"Expected {palettes.Length} tokenizer(s), one per theme of the renderer, got {tokenizers.Length}.", parameterName);
        }

        for (var i = 0; i < tokenizers.Length; i++)
        {
            var tokenizer = tokenizers[i] ?? throw new ArgumentException("A tokenizer is null.", parameterName);
            if (!ReferenceEquals(tokenizer.Theme, palettes[i].Theme))
            {
                throw new ArgumentException($"Tokenizer {i} uses the theme '{tokenizer.Theme.Name}', not the renderer's theme '{palettes[i].Theme.Name}' at that position.", parameterName);
            }

            if (i > 0 && tokenizer.ScopeName != tokenizers[0].ScopeName)
            {
                throw new ArgumentException($"Tokenizer {i} tokenizes '{tokenizer.ScopeName}', not '{tokenizers[0].ScopeName}'.", parameterName);
            }
        }
    }
}

#if NETSTANDARD2_0
internal sealed class LineTokensBuffer
{
    private readonly LineTokens[] _items = new LineTokens[ThemeSet.MaxThemes];

    public ref LineTokens this[int index] => ref _items[index];
}

internal sealed class StateBuffer
{
    private readonly StateStack[] _items = new StateStack[ThemeSet.MaxThemes];

    public ref StateStack this[int index] => ref _items[index];
}
#else
[InlineArray(ThemeSet.MaxThemes)]
internal struct LineTokensBuffer
{
    private LineTokens _element;
}

[InlineArray(ThemeSet.MaxThemes)]
internal struct StateBuffer
{
    private StateStack _element;
}
#endif
