using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Tint.Grammars;
using Tint.Internal.Selectors;

namespace Tint.Internal.Rules;

// What the rule compiler needs from the registry.
internal interface IGrammarRepository
{
    IGrammarSource? Lookup(string scopeName);

    IReadOnlyList<string> GetInjections(string targetScopeName);
}

// One initialized copy of a raw grammar (vscode-textmate's initGrammar clones the raw grammar per inclusion): rules
// are memoized per (instance, raw rule), and $base points at the $self of the grammar that included it first.
internal sealed class GrammarInstance
{
    public GrammarInstance(IGrammarSource source, RawGrammar grammar, GrammarInstance? baseOwner, RawRule? baseRule)
    {
        Source = source;
        Grammar = grammar;
        BaseOwner = baseOwner ?? this;
        BaseRule = baseRule ?? grammar.Self;
        Repository = new RepositoryScope(this, grammar.Repository, null);
    }

    public IGrammarSource Source { get; }

    public RawGrammar Grammar { get; }

    public GrammarInstance BaseOwner { get; }

    public RawRule BaseRule { get; }

    public RepositoryScope Repository { get; }
}

// A repository as vscode-textmate sees it: the grammar's own (with $self and $base), or a rule-local repository
// merged over the enclosing one (mergeObjects({}, repository, desc.repository)).
internal sealed class RepositoryScope
{
    private readonly GrammarInstance _owner;
    private readonly FrozenDictionary<string, RawRule> _rules;
    private readonly RepositoryScope? _outer;

    public RepositoryScope(GrammarInstance owner, FrozenDictionary<string, RawRule> rules, RepositoryScope? outer)
    {
        _owner = owner;
        _rules = rules;
        _outer = outer;
    }

    public GrammarInstance Owner => _owner;

    public bool TryLookup(string name, out GrammarInstance owner, out RawRule rule)
    {
        if (_outer is null)
        {
            // initGrammar assigns $self and $base over whatever the grammar's repository holds.
            if (name == "$self")
            {
                owner = _owner;
                rule = _owner.Grammar.Self;
                return true;
            }

            if (name == "$base")
            {
                owner = _owner.BaseOwner;
                rule = _owner.BaseRule;
                return true;
            }
        }

        if (_rules.TryGetValue(name, out var found))
        {
            owner = _owner;
            rule = found;
            return true;
        }

        if (_outer is not null)
        {
            return _outer.TryLookup(name, out owner, out rule);
        }

        owner = null!;
        rule = null!;
        return false;
    }
}

internal sealed class Injection
{
    public Injection(string selector, ScopeMatcher matcher, int priority, int ruleId)
    {
        Selector = selector;
        Matcher = matcher;
        Priority = priority;
        RuleId = ruleId;
    }

    public string Selector { get; }

    public ScopeMatcher Matcher { get; }

    // -1 for L:, 0 by default, 1 for R:.
    public int Priority { get; }

    public int RuleId { get; }
}

internal readonly record struct TokenTypeMatcher(ScopeMatcher Matcher, StandardTokenType Type);

internal sealed class BalancedBracketSelectors
{
    private readonly ScopeMatcher[] _balanced;
    private readonly ScopeMatcher[] _unbalanced;
    private readonly bool _allowAny;

    public BalancedBracketSelectors(IReadOnlyList<string> balanced, IReadOnlyList<string> unbalanced)
    {
        var balancedMatchers = new List<ScopeMatcher>();
        foreach (var selector in balanced)
        {
            if (selector == "*")
            {
                _allowAny = true;
                continue;
            }

            foreach (var matcher in ScopeSelector.CreateMatchers(selector, ScopeSelector.ScopeNameMatcher))
            {
                balancedMatchers.Add(matcher.Matcher);
            }
        }

        var unbalancedMatchers = new List<ScopeMatcher>();
        foreach (var selector in unbalanced)
        {
            foreach (var matcher in ScopeSelector.CreateMatchers(selector, ScopeSelector.ScopeNameMatcher))
            {
                unbalancedMatchers.Add(matcher.Matcher);
            }
        }

        _balanced = [.. balancedMatchers];
        _unbalanced = [.. unbalancedMatchers];
    }

    public bool MatchesAlways => _allowAny && _unbalanced.Length == 0;

    public bool MatchesNever => _balanced.Length == 0 && !_allowAny;

    public bool Match(ReadOnlySpan<string> scopes)
    {
        foreach (var excluder in _unbalanced)
        {
            if (excluder.Matches(scopes))
            {
                return false;
            }
        }

        foreach (var includer in _balanced)
        {
            if (includer.Matches(scopes))
            {
                return true;
            }
        }

        return _allowAny;
    }
}

// The scope stacks of one grammar under one theme share a root, so their nodes and child caches are shared too.
internal sealed class ScopeContext
{
    public ScopeContext(CompiledGrammar grammar, Theme theme)
    {
        Grammar = grammar;
        Theme = theme;
        var defaults = theme.Defaults;
        var defaultMetadata = EncodedTokenAttributes.Set(0, grammar.Attributes.DefaultAttributes.LanguageId, null, null, defaults.FontStyle, defaults.ForegroundId, defaults.BackgroundId);
        RootScopes = AttributedScopeStack.CreateRoot(grammar.Rules[grammar.RootId].Name ?? "unknown", defaultMetadata, this);
    }

    public CompiledGrammar Grammar { get; }

    public Theme Theme { get; }

    public AttributedScopeStack RootScopes { get; }
}

// A grammar compiled into rules (vscode-textmate's Grammar plus RuleFactory): rule ids are assigned eagerly from the
// root and injections, scanners are built lazily by the tokenizers. Immutable once constructed.
internal sealed class CompiledGrammar
{
    private readonly IGrammarRepository _repository;
    private readonly Dictionary<RuleKey, int> _ruleIds = new(RuleKeyComparer.Instance);
    private readonly Dictionary<string, GrammarInstance?> _includedGrammars = new(StringComparer.Ordinal);
    private readonly List<string> _missingGrammars = [];
    private readonly ConditionalWeakTable<Theme, ScopeContext> _contexts = new();

    public CompiledGrammar(
        IGrammarSource source,
        IGrammarRepository repository,
        int initialLanguageId,
        IEnumerable<KeyValuePair<string, int>> embeddedLanguages,
        IReadOnlyList<KeyValuePair<string, StandardTokenType>> tokenTypes,
        BalancedBracketSelectors? balancedBracketSelectors)
    {
        _repository = repository;
        ScopeName = source.ScopeName;
        Attributes = new BasicScopeAttributesProvider(initialLanguageId, embeddedLanguages);
        BalancedBracketSelectors = balancedBracketSelectors;

        var tokenTypeMatchers = new List<TokenTypeMatcher>();
        foreach (var tokenType in tokenTypes)
        {
            foreach (var matcher in ScopeSelector.CreateMatchers(tokenType.Key, ScopeSelector.ScopeNameMatcher))
            {
                tokenTypeMatchers.Add(new TokenTypeMatcher(matcher.Matcher, tokenType.Value));
            }
        }

        TokenTypeMatchers = [.. tokenTypeMatchers];

        var root = new GrammarInstance(source, source.GetGrammar(), null, null);
        RootId = GetCompiledRuleId(root.Grammar.Self, root, root.Repository);
        Injections = CollectInjections(root);
        MissingGrammars = [.. _missingGrammars];
    }

    public string ScopeName { get; }

    public RuleTable Rules { get; } = new();

    public int RootId { get; }

    public Injection[] Injections { get; }

    public BasicScopeAttributesProvider Attributes { get; }

    public TokenTypeMatcher[] TokenTypeMatchers { get; }

    public BalancedBracketSelectors? BalancedBracketSelectors { get; }

    public IReadOnlyList<string> MissingGrammars { get; }

    public ScopeContext GetContext(Theme theme)
    {
        if (_contexts.TryGetValue(theme, out var context))
        {
            return context;
        }

        return _contexts.GetValue(theme, t => new ScopeContext(this, t));
    }

    private Injection[] CollectInjections(GrammarInstance root)
    {
        var result = new List<Injection>();
        foreach (var injection in root.Grammar.Injections)
        {
            CollectInjections(result, injection.Key, injection.Value, root, root.Repository);
        }

        foreach (var injectionScopeName in _repository.GetInjections(ScopeName))
        {
            var injectionGrammar = GetExternalGrammar(injectionScopeName, null);
            if (injectionGrammar is null)
            {
                continue;
            }

            var selector = injectionGrammar.Source.Info.InjectionSelector ?? injectionGrammar.Grammar.InjectionSelector;
            if (selector is not null && selector.Length > 0)
            {
                CollectInjections(result, selector, injectionGrammar.Grammar.InjectionRule, injectionGrammar, injectionGrammar.Repository);
            }
        }

        // Sort by priority; OrderBy is stable, as JavaScript's sort.
        return [.. result.OrderBy(i => i.Priority)];
    }

    private void CollectInjections(List<Injection> result, string selector, RawRule rule, GrammarInstance owner, RepositoryScope repository)
    {
        var matchers = ScopeSelector.CreateMatchers(selector, ScopeSelector.ScopeNameMatcher);
        var ruleId = GetCompiledRuleId(rule, owner, repository);
        foreach (var matcher in matchers)
        {
            result.Add(new Injection(selector, matcher.Matcher, matcher.Priority, ruleId));
        }
    }

    // Grammar.getExternalGrammar: one instance per scope name, whose $base is the including repository's $base.
    private GrammarInstance? GetExternalGrammar(string scopeName, RepositoryScope? including)
    {
        if (_includedGrammars.TryGetValue(scopeName, out var cached))
        {
            return cached;
        }

        var source = _repository.Lookup(scopeName);
        if (source is null)
        {
            if (!_missingGrammars.Contains(scopeName))
            {
                _missingGrammars.Add(scopeName);
            }

            return null;
        }

        GrammarInstance? baseOwner = null;
        RawRule? baseRule = null;
        if (including is not null && including.TryLookup("$base", out var owner, out var rule))
        {
            baseOwner = owner;
            baseRule = rule;
        }

        var instance = new GrammarInstance(source, source.GetGrammar(), baseOwner, baseRule);
        _includedGrammars[scopeName] = instance;
        return instance;
    }

    // RuleFactory.getCompiledRuleId.
    private int GetCompiledRuleId(RawRule desc, GrammarInstance owner, RepositoryScope repository)
    {
        var key = new RuleKey(owner, desc);
        if (_ruleIds.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var id = Rules.Reserve();
        _ruleIds[key] = id;

        Rule rule;
        if (desc.Match is { } match)
        {
            rule = new MatchRule(id, desc.Name, match, CompileCaptures(desc.Captures, owner, repository));
        }
        else if (desc.Begin is null)
        {
            if (desc.Repository is { } local)
            {
                repository = new RepositoryScope(owner, local, repository);
            }

            var patterns = desc.Patterns;
            if (patterns is null && desc.Include is { } include)
            {
                patterns = [new RawRule { Include = include }];
            }

            var (ids, missing) = CompilePatterns(patterns, owner, repository);
            rule = new IncludeOnlyRule(id, desc.Name, desc.ContentName, ids, missing);
        }
        else if (desc.While is { } whilePattern)
        {
            var beginCaptures = CompileCaptures(desc.BeginCaptures ?? desc.Captures, owner, repository);
            var whileCaptures = CompileCaptures(desc.WhileCaptures ?? desc.Captures, owner, repository);
            var (ids, missing) = CompilePatterns(desc.Patterns, owner, repository);
            rule = new BeginWhileRule(id, desc.Name, desc.ContentName, desc.Begin, beginCaptures, whilePattern, whileCaptures, ids, missing);
        }
        else
        {
            var beginCaptures = CompileCaptures(desc.BeginCaptures ?? desc.Captures, owner, repository);
            var endCaptures = CompileCaptures(desc.EndCaptures ?? desc.Captures, owner, repository);
            var (ids, missing) = CompilePatterns(desc.Patterns, owner, repository);
            rule = new BeginEndRule(id, desc.Name, desc.ContentName, desc.Begin, beginCaptures, desc.End ?? RegexSource.Empty, endCaptures, desc.ApplyEndPatternLast, ids, missing);
        }

        rule.GrammarScopeName = owner.Grammar.ScopeName;
        Rules.Set(id, rule);
        return id;
    }

    // RuleFactory._compileCaptures.
    private CaptureRule?[] CompileCaptures(IReadOnlyList<RawRule?>? captures, GrammarInstance owner, RepositoryScope repository)
    {
        if (captures is null || captures.Count == 0)
        {
            return [];
        }

        var result = new CaptureRule?[captures.Count];
        for (var i = 0; i < captures.Count; i++)
        {
            if (captures[i] is not { } capture)
            {
                continue;
            }

            var retokenizeCapturedWithRuleId = 0;
            if (capture.Patterns is not null)
            {
                retokenizeCapturedWithRuleId = GetCompiledRuleId(capture, owner, repository);
            }

            var id = Rules.Reserve();
            var captureRule = new CaptureRule(id, capture.Name, capture.ContentName, retokenizeCapturedWithRuleId);
            captureRule.GrammarScopeName = owner.Grammar.ScopeName;
            Rules.Set(id, captureRule);
            result[i] = captureRule;
        }

        return result;
    }

    // RuleFactory._compilePatterns.
    private (int[] Ids, bool HasMissingPatterns) CompilePatterns(IReadOnlyList<RawRule>? patterns, GrammarInstance owner, RepositoryScope repository)
    {
        if (patterns is null)
        {
            return ([], false);
        }

        var result = new List<int>(patterns.Count);
        foreach (var pattern in patterns)
        {
            var ruleId = -1;
            if (pattern.Include is { } include)
            {
                switch (include.Kind)
                {
                    case IncludeReferenceKind.Base:
                    case IncludeReferenceKind.Self:
                        if (repository.TryLookup(include.Kind == IncludeReferenceKind.Base ? "$base" : "$self", out var selfOwner, out var selfRule))
                        {
                            ruleId = GetCompiledRuleId(selfRule, selfOwner, repository);
                        }

                        break;
                    case IncludeReferenceKind.Relative:
                        if (repository.TryLookup(include.RuleName!, out var localOwner, out var localRule))
                        {
                            ruleId = GetCompiledRuleId(localRule, localOwner, repository);
                        }

                        break;
                    case IncludeReferenceKind.TopLevel:
                    case IncludeReferenceKind.TopLevelRepository:
                        var external = GetExternalGrammar(include.ScopeName!, repository);
                        if (external is not null)
                        {
                            if (include.Kind == IncludeReferenceKind.TopLevelRepository)
                            {
                                if (external.Repository.TryLookup(include.RuleName!, out var externalOwner, out var externalRule))
                                {
                                    ruleId = GetCompiledRuleId(externalRule, externalOwner, external.Repository);
                                }
                            }
                            else
                            {
                                ruleId = GetCompiledRuleId(external.Grammar.Self, external, external.Repository);
                            }
                        }

                        break;
                }
            }
            else
            {
                ruleId = GetCompiledRuleId(pattern, owner, repository);
            }

            if (ruleId != -1)
            {
                // A rule still being compiled (a recursive reference) is not there yet and is kept.
                if (Rules.TryGet(ruleId) is RuleWithPatterns { HasMissingPatterns: true, Patterns.Length: 0 })
                {
                    continue;
                }

                result.Add(ruleId);
            }
        }

        return ([.. result], patterns.Count != result.Count);
    }

    private readonly record struct RuleKey(GrammarInstance Owner, RawRule Rule);

    private sealed class RuleKeyComparer : IEqualityComparer<RuleKey>
    {
        public static readonly RuleKeyComparer Instance = new();

        public bool Equals(RuleKey x, RuleKey y) => ReferenceEquals(x.Owner, y.Owner) && ReferenceEquals(x.Rule, y.Rule);

        public int GetHashCode(RuleKey obj) => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Owner), RuntimeHelpers.GetHashCode(obj.Rule));
    }
}
