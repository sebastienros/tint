using System.Diagnostics.CodeAnalysis;
using Tint.Grammars;
using Tint.Internal.Rules;

namespace Tint;

/// <summary>
/// The grammars a document can use: sources by scope name, injections, embedded language ids, and the compiled
/// grammars shared by the tokenizers it creates. Registration is not thread-safe; creating tokenizers is.
/// </summary>
public sealed class GrammarRegistry
{
    private readonly Dictionary<string, IGrammarSource> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _injections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _languageIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompiledGrammar> _compiled = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>Registers a grammar, its language and the grammars it injects into. A grammar with the same scope name is replaced.</summary>
    public void Add(IGrammarSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_lock)
        {
            _sources[source.ScopeName] = source;
            if (source.Info.LanguageId is { } languageId)
            {
                GetLanguageIdCore(languageId);
            }

            foreach (var target in source.Info.InjectTo)
            {
                AddInjectionCore(target, source.ScopeName);
            }

            _compiled.Clear();
        }
    }

    /// <summary>Registers grammars in order.</summary>
    public void Add(params IGrammarSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var source in sources)
        {
            Add(source);
        }
    }

    /// <summary>Finds a registered grammar.</summary>
    public bool TryGet(string scopeName, [NotNullWhen(true)] out IGrammarSource? source)
    {
        ArgumentNullException.ThrowIfNull(scopeName);
        lock (_lock)
        {
            return _sources.TryGetValue(scopeName, out source);
        }
    }

    /// <summary>
    /// Makes the grammar <paramref name="injectionScopeName"/> inject into <paramref name="targetScopeName"/>, as
    /// <see cref="GrammarInfo.InjectTo"/> does.
    /// </summary>
    public void AddInjection(string targetScopeName, string injectionScopeName)
    {
        ArgumentNullException.ThrowIfNull(targetScopeName);
        ArgumentNullException.ThrowIfNull(injectionScopeName);
        lock (_lock)
        {
            AddInjectionCore(targetScopeName, injectionScopeName);
            _compiled.Clear();
        }
    }

    /// <summary>The injection grammars registered for a scope, in registration order.</summary>
    public IReadOnlyList<string> GetInjections(string targetScopeName)
    {
        ArgumentNullException.ThrowIfNull(targetScopeName);
        lock (_lock)
        {
            return _injections.TryGetValue(targetScopeName, out var list) ? [.. list] : [];
        }
    }

    /// <summary>
    /// The numeric id of a language, as stored in <see cref="EncodedTokenAttributes"/>: 1-based, assigned in the order
    /// languages are first seen (registered grammars' languages, then calls to this method, then embedded languages).
    /// </summary>
    public int GetLanguageId(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        lock (_lock)
        {
            return GetLanguageIdCore(languageId);
        }
    }

    /// <summary>Creates a tokenizer for a registered grammar.</summary>
    /// <param name="scopeName">The root scope name of the grammar.</param>
    /// <param name="theme">The theme to resolve token colors with, or null for <see cref="Theme.Empty"/>.</param>
    public Tokenizer CreateTokenizer(string scopeName, Theme? theme = null)
        => new(GetCompiledGrammar(scopeName), theme ?? Theme.Empty);

    /// <summary>
    /// The scope names a grammar includes, directly or through the grammars it includes, that are not registered.
    /// Their rules are skipped, as vscode-textmate does.
    /// </summary>
    public IReadOnlyList<string> GetMissingGrammars(string scopeName) => GetCompiledGrammar(scopeName).MissingGrammars;

    private CompiledGrammar GetCompiledGrammar(string scopeName)
    {
        ArgumentNullException.ThrowIfNull(scopeName);
        lock (_lock)
        {
            if (_compiled.TryGetValue(scopeName, out var compiled))
            {
                return compiled;
            }

            if (!_sources.TryGetValue(scopeName, out var source))
            {
                throw new KeyNotFoundException($"No grammar is registered for the scope '{scopeName}'.");
            }

            var info = source.Info;
            var initialLanguageId = info.LanguageId is { } languageId ? GetLanguageIdCore(languageId) : 0;
            var embeddedLanguages = new List<KeyValuePair<string, int>>();
            foreach (var embedded in info.EmbeddedLanguages)
            {
                embeddedLanguages.Add(new KeyValuePair<string, int>(embedded.Key, GetLanguageIdCore(embedded.Value)));
            }

            var brackets = new BalancedBracketSelectors(info.BalancedBracketScopes ?? ["*"], info.UnbalancedBracketScopes);
            compiled = new CompiledGrammar(source, new Repository(this), initialLanguageId, embeddedLanguages, info.TokenTypes, brackets);
            _compiled[scopeName] = compiled;
            return compiled;
        }
    }

    private int GetLanguageIdCore(string languageId)
    {
        if (!_languageIds.TryGetValue(languageId, out var id))
        {
            id = _languageIds.Count + 1;
            _languageIds[languageId] = id;
        }

        return id;
    }

    private void AddInjectionCore(string target, string injection)
    {
        if (!_injections.TryGetValue(target, out var list))
        {
            list = [];
            _injections[target] = list;
        }

        if (!list.Contains(injection))
        {
            list.Add(injection);
        }
    }

    // Called under the registry lock while a grammar compiles.
    private sealed class Repository : IGrammarRepository
    {
        private readonly GrammarRegistry _registry;

        public Repository(GrammarRegistry registry)
        {
            _registry = registry;
        }

        public IGrammarSource? Lookup(string scopeName) => _registry._sources.GetValueOrDefault(scopeName);

        public IReadOnlyList<string> GetInjections(string targetScopeName)
            => _registry._injections.TryGetValue(targetScopeName, out var list) ? list : [];
    }
}
