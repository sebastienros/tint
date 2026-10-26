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
    private const int MaxLanguageId = 255;

    private readonly object _lock = new();
    private TokenizerOptions _tokenizerOptions = TokenizerOptions.Default;

    /// <summary>
    /// Registers a grammar, its language and the grammars it injects into. A grammar with the same scope name is
    /// replaced, along with the injections it declared; tokenizers created before keep the grammar they compiled.
    /// </summary>
    /// <exception cref="InvalidOperationException">The grammar's language would need an id above 255.</exception>
    public void Add(IGrammarSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_lock)
        {
            // A replaced grammar no longer injects where it used to.
            if (_sources.TryGetValue(source.ScopeName, out var previous))
            {
                foreach (var target in previous.Info.InjectTo)
                {
                    if (_injections.TryGetValue(target, out var list))
                    {
                        list.Remove(previous.ScopeName);
                    }
                }
            }

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
    /// <exception cref="InvalidOperationException">The language would need an id above 255, the largest the 8-bit field holds.</exception>
    public int GetLanguageId(string languageId)
    {
        ArgumentNullException.ThrowIfNull(languageId);
        lock (_lock)
        {
            return GetLanguageIdCore(languageId);
        }
    }

    /// <summary>
    /// Creates a tokenizer for a registered grammar. The grammar is compiled once per registry and shared; the
    /// tokenizer builds its own scanners as it reaches rules, so keep it alive (one per thread or document) rather
    /// than creating one per call.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No grammar is registered for the scope.</exception>
    /// <exception cref="InvalidOperationException">An embedded language would need an id above 255.</exception>
    /// <param name="scopeName">The root scope name of the grammar.</param>
    /// <param name="theme">The theme to resolve token colors with, or null for <see cref="Theme.Empty"/>.</param>
    public Tokenizer CreateTokenizer(string scopeName, Theme? theme = null)
        => new(GetCompiledGrammar(scopeName), theme ?? Theme.Empty, TokenizerOptions);

    /// <summary>
    /// Creates a tokenizer for a registered grammar with its own options; see <see cref="CreateTokenizer(string, Theme?)"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No grammar is registered for the scope.</exception>
    /// <exception cref="InvalidOperationException">An embedded language would need an id above 255.</exception>
    /// <param name="scopeName">The root scope name of the grammar.</param>
    /// <param name="theme">The theme to resolve token colors with, or null for <see cref="Theme.Empty"/>.</param>
    /// <param name="options">The tokenizer options.</param>
    public Tokenizer CreateTokenizer(string scopeName, Theme? theme, TokenizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(GetCompiledGrammar(scopeName), theme ?? Theme.Empty, options);
    }

    /// <summary>
    /// Creates a highlighter for a registered grammar and one theme. Like a tokenizer, a highlighter is not
    /// thread-safe: keep one per thread and reuse it.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No grammar is registered for the scope.</exception>
    /// <param name="scopeName">The root scope name of the grammar.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="options">Renderer and tokenizer settings, or null for the defaults.</param>
    public Highlighter CreateHighlighter(string scopeName, Theme theme, HighlighterOptions? options = null)
        => CreateHighlighter(scopeName, theme, [], options);

    /// <summary>
    /// Creates a highlighter for a light and a dark theme: HTML carries the light colors inline and the dark ones as
    /// <c>--tint-dark</c> CSS variables (see <see cref="Rendering.HtmlRenderer"/>); terminal output and tokens use the
    /// light theme.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No grammar is registered for the scope.</exception>
    /// <param name="scopeName">The root scope name of the grammar.</param>
    /// <param name="light">The light theme, the default.</param>
    /// <param name="dark">The dark theme, named <c>dark</c>.</param>
    /// <param name="options">Renderer and tokenizer settings, or null for the defaults.</param>
    public Highlighter CreateHighlighter(string scopeName, Theme light, Theme dark, HighlighterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dark);
        return CreateHighlighter(scopeName, light, [new KeyValuePair<string, Theme>("dark", dark)], options);
    }

    /// <summary>Creates a highlighter for a default theme and named additional themes, written by HTML as CSS variables.</summary>
    /// <exception cref="KeyNotFoundException">No grammar is registered for the scope.</exception>
    /// <exception cref="ArgumentException">A theme name is not a CSS identifier or appears twice, or there are more than eight themes.</exception>
    /// <param name="scopeName">The root scope name of the grammar.</param>
    /// <param name="defaultTheme">The default theme.</param>
    /// <param name="additionalThemes">The other themes by name.</param>
    /// <param name="options">Renderer and tokenizer settings, or null for the defaults.</param>
    public Highlighter CreateHighlighter(string scopeName, Theme defaultTheme, IEnumerable<KeyValuePair<string, Theme>> additionalThemes, HighlighterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(defaultTheme);
        ArgumentNullException.ThrowIfNull(additionalThemes);
        options ??= HighlighterOptions.Default;
        var tokenizerOptions = options.Tokenizer ?? TokenizerOptions;
        var themes = additionalThemes.ToArray();
        var tokenizers = new Tokenizer[themes.Length + 1];
        tokenizers[0] = CreateTokenizer(scopeName, defaultTheme, tokenizerOptions);
        for (var i = 0; i < themes.Length; i++)
        {
            tokenizers[i + 1] = CreateTokenizer(scopeName, themes[i].Value ?? throw new ArgumentException("A theme is null.", nameof(additionalThemes)), tokenizerOptions);
        }

        return new Highlighter(tokenizers, themes, options);
    }

    /// <summary>
    /// The options of the tokenizers and highlighters this registry creates when none are given. Defaults to
    /// <see cref="TokenizerOptions.Default"/>.
    /// </summary>
    public TokenizerOptions TokenizerOptions
    {
        get => _tokenizerOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _tokenizerOptions = value;
        }
    }

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
            if (id > MaxLanguageId)
            {
                throw new InvalidOperationException($"Cannot assign an id to the language '{languageId}': encoded token attributes hold at most {MaxLanguageId} languages.");
            }

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
