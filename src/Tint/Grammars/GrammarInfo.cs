using System.Collections.Frozen;

namespace Tint.Grammars;

/// <summary>
/// Metadata that VS Code reads from an extension's <c>package.json</c> rather than from the grammar file itself.
/// </summary>
public sealed class GrammarInfo
{
    /// <summary>Metadata with no language, no embedded languages, no injections and no token types.</summary>
    public static GrammarInfo Empty { get; } = new();

    /// <summary>The language identifier, for example <c>csharp</c>, or null for grammars without a language.</summary>
    public string? LanguageId { get; init; }

    /// <summary>Display names of the language.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>File extensions, with the leading dot.</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = [];

    /// <summary>Scope name to language identifier for the languages embedded in this grammar.</summary>
    public IReadOnlyDictionary<string, string> EmbeddedLanguages { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>Scope names of the grammars this grammar injects into.</summary>
    public IReadOnlyList<string> InjectTo { get; init; } = [];

    /// <summary>
    /// The injection selector, when it is not in the grammar file. The grammar's own <c>injectionSelector</c> is used
    /// when this is null.
    /// </summary>
    public string? InjectionSelector { get; init; }

    /// <summary>Scope selectors that override the standard token type, in declaration order (later wins).</summary>
    public IReadOnlyList<KeyValuePair<string, StandardTokenType>> TokenTypes { get; init; } = [];

    /// <summary>
    /// Scope selectors of tokens whose brackets are balanced. Null means every scope (<c>*</c>), the VS Code default.
    /// </summary>
    public IReadOnlyList<string>? BalancedBracketScopes { get; init; }

    /// <summary>Scope selectors of tokens whose brackets are not balanced; they win over the balanced ones.</summary>
    public IReadOnlyList<string> UnbalancedBracketScopes { get; init; } = [];

    /// <summary>Scope names of other grammars this grammar includes.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
