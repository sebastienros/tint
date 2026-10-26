using System.Collections.Frozen;

namespace Tint.Grammars;

/// <summary>
/// The rule tree of a TextMate grammar: root patterns, repository and injections. Immutable.
/// </summary>
public sealed class RawGrammar
{
    /// <summary>Creates a grammar.</summary>
    /// <param name="scopeName">The root scope name.</param>
    /// <param name="patterns">The root patterns.</param>
    /// <param name="repository">The named rules, or null for none.</param>
    /// <param name="injections">Injection selectors and their rules, in declaration order, or null for none.</param>
    /// <param name="injectionSelector">The selector of an injection grammar, or null.</param>
    /// <param name="name">The display name of the grammar, or null.</param>
    public RawGrammar(
        string scopeName,
        IReadOnlyList<RawRule> patterns,
        FrozenDictionary<string, RawRule>? repository = null,
        IReadOnlyList<KeyValuePair<string, RawRule>>? injections = null,
        string? injectionSelector = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scopeName);
        ArgumentNullException.ThrowIfNull(patterns);
        ScopeName = scopeName;
        Patterns = patterns;
        Repository = repository ?? FrozenDictionary<string, RawRule>.Empty;
        Injections = injections ?? [];
        InjectionSelector = injectionSelector;
        Name = name;

        // initGrammar: repository.$self = { patterns, name: scopeName }.
        Self = new RawRule { Patterns = patterns, Name = scopeName };
        InjectionRule = new RawRule { Patterns = patterns, Name = name };
    }

    /// <summary>The root scope name.</summary>
    public string ScopeName { get; }

    /// <summary>The display name of the grammar.</summary>
    public string? Name { get; }

    /// <summary>The root patterns.</summary>
    public IReadOnlyList<RawRule> Patterns { get; }

    /// <summary>The named rules, referenced by <c>#name</c> includes.</summary>
    public FrozenDictionary<string, RawRule> Repository { get; }

    /// <summary>Injection selectors and their rules, in declaration order.</summary>
    public IReadOnlyList<KeyValuePair<string, RawRule>> Injections { get; }

    /// <summary>The selector of an injection grammar, telling where its patterns apply.</summary>
    public string? InjectionSelector { get; }

    /// <summary>
    /// The <c>$self</c> rule: the root patterns under the root scope name. <c>$base</c> refers to the <c>$self</c> of the
    /// grammar the tokenizer started from, so it is resolved when rules are compiled.
    /// </summary>
    public RawRule Self { get; }

    // vscode-textmate compiles an injection grammar as a rule (the grammar object itself): its patterns, without the
    // root scope name.
    internal RawRule InjectionRule { get; }
}
