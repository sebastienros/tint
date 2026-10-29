using Tint.Internal;
using Tint.Themes;

namespace Tint;

/// <summary>
/// A resolved theme, a port of vscode-textmate's <c>theme.ts</c>: rules sorted and inserted in a trie keyed by scope
/// segments, matched against a scope stack by the most specific rule whose parent scopes match. Thread-safe.
/// </summary>
public sealed class Theme
{
    private static readonly string[] s_noParentScopes = [];
    private static readonly string[] s_globalScope = [string.Empty];
    private readonly ThemeTrieElement _root;
    // Scope names derived from captures are unbounded; the cache stops growing at this size.
    internal const int MatchCacheCapacity = 4096;

    private readonly BoundedCache<ThemeTrieElementRule[]> _cachedMatches = new(MatchCacheCapacity);

    private Theme(ColorMap colorMap, StyleAttributes defaults, ThemeTrieElement root, string? name, IReadOnlyDictionary<string, string> uiColors)
    {
        ColorMap = colorMap;
        Defaults = defaults;
        _root = root;
        Name = name;
        UiColors = uiColors;
    }

    /// <summary>A theme without rules: black on white.</summary>
    public static Theme Empty { get; } = FromSource(new RawTheme([]));

    /// <summary>The display name.</summary>
    public string? Name { get; }

    /// <summary>The colors used by the theme's rules.</summary>
    public ColorMap ColorMap { get; }

    /// <summary>The style of text no rule matches: the global rule's settings, <c>#000000</c> on <c>#ffffff</c> otherwise.</summary>
    public StyleAttributes Defaults { get; }

    /// <summary>Workbench colors, for example <c>editor.background</c>.</summary>
    public IReadOnlyDictionary<string, string> UiColors { get; }

    /// <summary>Resolves a theme.</summary>
    /// <exception cref="InvalidOperationException">The theme uses more colors than encoded token attributes hold (511 foreground ids, 255 background ids).</exception>
    public static Theme FromSource(IThemeSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var raw = source.GetTheme();
        return Resolve(Parse(raw), raw.Name ?? source.Name, raw.UiColors);
    }

    /// <summary>The style for the innermost scope of a scope stack, or null when no rule applies.</summary>
    public StyleAttributes? Match(AttributedScopeStack scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        return Match(scopes.ScopeName, new NodeCursor(scopes.Parent));
    }

    /// <summary>The style for the last scope of a scope path (root first), or null when no rule applies.</summary>
    public StyleAttributes? Match(IReadOnlyList<string> scopePath)
    {
        ArgumentNullException.ThrowIfNull(scopePath);
        if (scopePath.Count == 0)
        {
            return Defaults;
        }

        return Match(scopePath[^1], new ListCursor(scopePath, scopePath.Count - 2));
    }

    internal ThemeTrieElement Root => _root;

    internal int CachedMatchCount => _cachedMatches.Count;

    internal StyleAttributes? Match(string scopeName, AttributedScopeStack? parent) => Match(scopeName, new NodeCursor(parent));

    private StyleAttributes? Match<TCursor>(string scopeName, TCursor parent)
        where TCursor : struct, IScopeCursor
    {
        var rules = _cachedMatches.GetOrAdd(scopeName, static (name, root) => root.Match(name), _root);
        foreach (var rule in rules)
        {
            if (ScopePathMatchesParentScopes(parent, rule.ParentScopes))
            {
                return new StyleAttributes((FontStyle)rule.FontStyle, rule.Foreground, rule.Background);
            }
        }

        return null;
    }

    private static bool ScopePathMatchesParentScopes<TCursor>(TCursor scopePath, string[] parentScopes)
        where TCursor : struct, IScopeCursor
    {
        if (parentScopes.Length == 0)
        {
            return true;
        }

        // Starting with the deepest parent scope, look for a match in the scope path.
        for (var index = 0; index < parentScopes.Length; index++)
        {
            var scopePattern = parentScopes[index];
            var scopeMustMatch = false;

            // A child combinator (a parent-child relationship).
            if (scopePattern == ">")
            {
                if (index == parentScopes.Length - 1)
                {
                    return false;
                }

                scopePattern = parentScopes[++index];
                scopeMustMatch = true;
            }

            while (scopePath.HasValue)
            {
                if (MatchesScope(scopePath.Name, scopePattern))
                {
                    break;
                }

                if (scopeMustMatch)
                {
                    return false;
                }

                scopePath.MoveToParent();
            }

            if (!scopePath.HasValue)
            {
                return false;
            }

            scopePath.MoveToParent();
        }

        return true;
    }

    private static bool MatchesScope(string scopeName, string scopePattern)
        => scopePattern == scopeName
        || (scopeName.Length > scopePattern.Length && scopeName.StartsWith(scopePattern, StringComparison.Ordinal) && scopeName[scopePattern.Length] == '.');

    // parseTheme.
    internal static List<ParsedThemeRule> Parse(RawTheme theme)
    {
        var result = new List<ParsedThemeRule>();
        for (var i = 0; i < theme.Rules.Count; i++)
        {
            var entry = theme.Rules[i];
            var scopes = entry.Scopes ?? s_globalScope;

            var fontStyle = FontStyle.NotSet;
            if (entry.FontStyle is { } fontStyleText)
            {
                fontStyle = FontStyle.None;
                foreach (var segment in fontStyleText.Split(' '))
                {
                    fontStyle |= segment switch
                    {
                        "italic" => FontStyle.Italic,
                        "bold" => FontStyle.Bold,
                        "underline" => FontStyle.Underline,
                        "strikethrough" => FontStyle.Strikethrough,
                        _ => FontStyle.None,
                    };
                }
            }

            var foreground = entry.Foreground is { } fg && IsValidHexColor(fg) ? fg : null;
            var background = entry.Background is { } bg && IsValidHexColor(bg) ? bg : null;

            foreach (var selector in scopes)
            {
                var segments = selector.Trim().Split(' ');
                var scope = segments[^1];
                string[]? parentScopes = null;
                if (segments.Length > 1)
                {
                    parentScopes = segments.AsSpan(0, segments.Length - 1).ToArray();
                    Array.Reverse(parentScopes);
                }

                result.Add(new ParsedThemeRule(scope, parentScopes, i, fontStyle, foreground, background));
            }
        }

        return result;
    }

    // resolveParsedThemeRules.
    internal static Theme Resolve(List<ParsedThemeRule> rules, string? name = null, IReadOnlyDictionary<string, string>? uiColors = null)
    {
        // Sort rules lexicographically, then by index; Array.Sort is not stable, the index makes the order total.
        var sorted = rules.ToArray();
        Array.Sort(sorted, static (a, b) =>
        {
            var r = string.CompareOrdinal(a.Scope, b.Scope);
            if (r != 0)
            {
                return r;
            }

            r = CompareParentScopes(a.ParentScopes, b.ParentScopes);
            return r != 0 ? r : a.Index.CompareTo(b.Index);
        });

        var defaultFontStyle = FontStyle.None;
        var defaultForeground = "#000000";
        var defaultBackground = "#ffffff";
        var first = 0;
        while (first < sorted.Length && sorted[first].Scope.Length == 0)
        {
            var incoming = sorted[first++];
            if (incoming.FontStyle != FontStyle.NotSet)
            {
                defaultFontStyle = incoming.FontStyle;
            }

            if (incoming.Foreground is not null)
            {
                defaultForeground = incoming.Foreground;
            }

            if (incoming.Background is not null)
            {
                defaultBackground = incoming.Background;
            }
        }

        var colorMap = new ColorMap();
        var defaults = new StyleAttributes(defaultFontStyle, colorMap.GetOrAddId(defaultForeground, ColorMap.MaxForegroundId), colorMap.GetOrAddId(defaultBackground, ColorMap.MaxBackgroundId));

        var root = new ThemeTrieElement(new ThemeTrieElementRule(0, null, (int)FontStyle.NotSet, 0, 0), []);
        for (var i = first; i < sorted.Length; i++)
        {
            var rule = sorted[i];
            root.Insert(0, rule.Scope, rule.ParentScopes, (int)rule.FontStyle, colorMap.GetOrAddId(rule.Foreground, ColorMap.MaxForegroundId), colorMap.GetOrAddId(rule.Background, ColorMap.MaxBackgroundId));
        }

        return new Theme(colorMap, defaults, root, name, uiColors ?? new Dictionary<string, string>());
    }

    // strArrCmp.
    internal static int CompareParentScopes(string[]? a, string[]? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        if (a.Length == b.Length)
        {
            for (var i = 0; i < a.Length; i++)
            {
                var r = string.CompareOrdinal(a[i], b[i]);
                if (r != 0)
                {
                    return r < 0 ? -1 : 1;
                }
            }

            return 0;
        }

        return a.Length - b.Length;
    }

    // #rgb, #rgba, #rrggbb, #rrggbbaa.
    internal static bool IsValidHexColor(string hex)
    {
        if (hex.Length is not (4 or 5 or 7 or 9) || hex[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < hex.Length; i++)
        {
            if (!char.IsAsciiHexDigit(hex[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal readonly record struct ParsedThemeRule(string Scope, string[]? ParentScopes, int Index, FontStyle FontStyle, string? Foreground, string? Background);

    private interface IScopeCursor
    {
        bool HasValue { get; }

        string Name { get; }

        void MoveToParent();
    }

    private struct NodeCursor : IScopeCursor
    {
        private AttributedScopeStack? _node;

        public NodeCursor(AttributedScopeStack? node)
        {
            _node = node;
        }

        public readonly bool HasValue => _node is not null;

        public readonly string Name => _node!.ScopeName;

        public void MoveToParent() => _node = _node!.Parent;
    }

    private struct ListCursor : IScopeCursor
    {
        private readonly IReadOnlyList<string> _path;
        private int _index;

        public ListCursor(IReadOnlyList<string> path, int index)
        {
            _path = path;
            _index = index;
        }

        public readonly bool HasValue => _index >= 0;

        public readonly string Name => _path[_index];

        public void MoveToParent() => _index--;
    }

    internal sealed class ThemeTrieElementRule
    {
        public ThemeTrieElementRule(int scopeDepth, string[]? parentScopes, int fontStyle, int foreground, int background)
        {
            ScopeDepth = scopeDepth;
            ParentScopes = parentScopes ?? s_noParentScopes;
            FontStyle = fontStyle;
            Foreground = foreground;
            Background = background;
        }

        public int ScopeDepth { get; private set; }

        public string[] ParentScopes { get; }

        public int FontStyle { get; private set; }

        public int Foreground { get; private set; }

        public int Background { get; private set; }

        public ThemeTrieElementRule Clone() => new(ScopeDepth, ParentScopes, FontStyle, Foreground, Background);

        public void AcceptOverwrite(int scopeDepth, int fontStyle, int foreground, int background)
        {
            if (ScopeDepth <= scopeDepth)
            {
                ScopeDepth = scopeDepth;
            }

            if (fontStyle != (int)Tint.FontStyle.NotSet)
            {
                FontStyle = fontStyle;
            }

            if (foreground != 0)
            {
                Foreground = foreground;
            }

            if (background != 0)
            {
                Background = background;
            }
        }
    }

    internal sealed class ThemeTrieElement
    {
        private readonly ThemeTrieElementRule _mainRule;
        private readonly List<ThemeTrieElementRule> _rulesWithParentScopes;
        private readonly Dictionary<string, ThemeTrieElement> _children = new(StringComparer.Ordinal);

        public ThemeTrieElement(ThemeTrieElementRule mainRule, List<ThemeTrieElementRule> rulesWithParentScopes)
        {
            _mainRule = mainRule;
            _rulesWithParentScopes = rulesWithParentScopes;
        }

        internal ThemeTrieElementRule MainRule => _mainRule;

        internal IReadOnlyList<ThemeTrieElementRule> RulesWithParentScopes => _rulesWithParentScopes;

        internal IReadOnlyDictionary<string, ThemeTrieElement> Children => _children;

        public ThemeTrieElementRule[] Match(string scope)
        {
            var node = this;
            while (scope.Length != 0)
            {
                var dot = scope.IndexOf('.');
                var head = dot == -1 ? scope : scope.Substring(0, dot);
                if (!node._children.TryGetValue(head, out var child))
                {
                    break;
                }

                node = child;
                scope = dot == -1 ? string.Empty : scope.Substring(dot + 1);
            }

            var rules = new ThemeTrieElementRule[node._rulesWithParentScopes.Count + 1];
            node._rulesWithParentScopes.CopyTo(rules);
            rules[^1] = node._mainRule;

            // A stable insertion sort: JavaScript's sort is stable and ties keep declaration order.
            for (var i = 1; i < rules.Length; i++)
            {
                var current = rules[i];
                var j = i - 1;
                while (j >= 0 && CompareBySpecificity(rules[j], current) > 0)
                {
                    rules[j + 1] = rules[j];
                    j--;
                }

                rules[j + 1] = current;
            }

            return rules;
        }

        private static int CompareBySpecificity(ThemeTrieElementRule a, ThemeTrieElementRule b)
        {
            // Deeper scope first.
            if (a.ScopeDepth != b.ScopeDepth)
            {
                return b.ScopeDepth - a.ScopeDepth;
            }

            // Then parent scopes, nearest first, the longer name winning; child combinators don't count.
            var aParentIndex = 0;
            var bParentIndex = 0;
            while (true)
            {
                if (aParentIndex < a.ParentScopes.Length && a.ParentScopes[aParentIndex] == ">")
                {
                    aParentIndex++;
                }

                if (bParentIndex < b.ParentScopes.Length && b.ParentScopes[bParentIndex] == ">")
                {
                    bParentIndex++;
                }

                if (aParentIndex >= a.ParentScopes.Length || bParentIndex >= b.ParentScopes.Length)
                {
                    break;
                }

                var lengthDiff = b.ParentScopes[bParentIndex].Length - a.ParentScopes[aParentIndex].Length;
                if (lengthDiff != 0)
                {
                    return lengthDiff;
                }

                aParentIndex++;
                bParentIndex++;
            }

            // Then more parent scopes.
            return b.ParentScopes.Length - a.ParentScopes.Length;
        }

        public void Insert(int scopeDepth, string scope, string[]? parentScopes, int fontStyle, int foreground, int background)
        {
            if (scope.Length == 0)
            {
                DoInsertHere(scopeDepth, parentScopes, fontStyle, foreground, background);
                return;
            }

            var dot = scope.IndexOf('.');
            var head = dot == -1 ? scope : scope.Substring(0, dot);
            var tail = dot == -1 ? string.Empty : scope.Substring(dot + 1);

            if (!_children.TryGetValue(head, out var child))
            {
                var inherited = new List<ThemeTrieElementRule>(_rulesWithParentScopes.Count);
                foreach (var rule in _rulesWithParentScopes)
                {
                    inherited.Add(rule.Clone());
                }

                child = new ThemeTrieElement(_mainRule.Clone(), inherited);
                _children[head] = child;
            }

            child.Insert(scopeDepth + 1, tail, parentScopes, fontStyle, foreground, background);
        }

        private void DoInsertHere(int scopeDepth, string[]? parentScopes, int fontStyle, int foreground, int background)
        {
            if (parentScopes is null)
            {
                _mainRule.AcceptOverwrite(scopeDepth, fontStyle, foreground, background);
                return;
            }

            foreach (var rule in _rulesWithParentScopes)
            {
                if (CompareParentScopes(rule.ParentScopes, parentScopes) == 0)
                {
                    rule.AcceptOverwrite(scopeDepth, fontStyle, foreground, background);
                    return;
                }
            }

            // A new rule inherits what it does not set from the main rule.
            if (fontStyle == (int)FontStyle.NotSet)
            {
                fontStyle = _mainRule.FontStyle;
            }

            if (foreground == 0)
            {
                foreground = _mainRule.Foreground;
            }

            if (background == 0)
            {
                background = _mainRule.Background;
            }

            _rulesWithParentScopes.Add(new ThemeTrieElementRule(scopeDepth, parentScopes, fontStyle, foreground, background));
        }
    }
}
