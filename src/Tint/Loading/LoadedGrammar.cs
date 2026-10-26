using Tint.Grammars;

namespace Tint.Loading;

/// <summary>A grammar read from a file by <see cref="GrammarLoader"/>. Its regexes are parsed on first use.</summary>
public sealed class LoadedGrammar : IGrammarSource
{
    private readonly RawGrammar _grammar;

    internal LoadedGrammar(RawGrammar grammar, GrammarInfo info)
    {
        _grammar = grammar;
        Info = info;
    }

    /// <inheritdoc/>
    public string ScopeName => _grammar.ScopeName;

    /// <inheritdoc/>
    public GrammarInfo Info { get; }

    /// <inheritdoc/>
    public RawGrammar GetGrammar() => _grammar;
}
