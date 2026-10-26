namespace Tint.Grammars;

/// <summary>
/// A TextMate grammar as the tokenizer consumes it, whatever its origin: loaded from a file, generated at build time
/// or built in code.
/// </summary>
public interface IGrammarSource
{
    /// <summary>The root scope name of the grammar, for example <c>source.cs</c>.</summary>
    string ScopeName { get; }

    /// <summary>Metadata from the contributing extension: language, embedded languages, injections, token types.</summary>
    GrammarInfo Info { get; }

    /// <summary>The immutable rule tree. Built once and cached by the source.</summary>
    RawGrammar GetGrammar();
}
