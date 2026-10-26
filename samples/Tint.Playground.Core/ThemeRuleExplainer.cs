using Tint.Themes;

namespace Tint.Playground;

/// <summary>A rule of a theme's <c>tokenColors</c> and the scope of the token's stack it matched.</summary>
/// <param name="Index">The rule's position in the theme's rules.</param>
/// <param name="Name">The rule's name, when it has one.</param>
/// <param name="Selector">The selector of the rule that matched (one of its comma-separated scopes).</param>
/// <param name="MatchedScope">The scope of the token's stack the selector matched.</param>
/// <param name="Foreground">The rule's foreground, as the theme spells it.</param>
/// <param name="FontStyle">The rule's font style, as the theme spells it.</param>
public sealed record MatchedThemeRule(int Index, string? Name, string Selector, string MatchedScope, string? Foreground, string? FontStyle);

/// <summary>Why a token has its style: the rules that gave its foreground and its font style.</summary>
/// <param name="Foreground">The rule that set the foreground, or null when the theme's default applies.</param>
/// <param name="FontStyle">The rule that set the font style, or null when the theme's default applies.</param>
public sealed record ThemeExplanation(MatchedThemeRule? Foreground, MatchedThemeRule? FontStyle);

/// <summary>
/// Explains a token's style against the theme's rules, like shiki's <c>includeExplanation</c>. The tokenizer stores
/// the resolved style at every level of the scope stack, so the deepest level whose foreground (or font style)
/// differs from its parent's is the scope that set it; the rule is then the most specific selector matching that
/// scope, its parents and that value.
/// </summary>
public sealed class ThemeRuleExplainer
{
    private readonly Theme _theme;
    private readonly List<Selector> _selectors = [];

    /// <summary>Prepares the rules of <paramref name="source"/>, resolved as <paramref name="theme"/>.</summary>
    public ThemeRuleExplainer(IThemeSource source, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        var rules = source.GetTheme().Rules;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.Scopes is null)
            {
                continue;
            }

            foreach (var scope in rule.Scopes)
            {
                var segments = scope.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                {
                    _selectors.Add(new Selector(i, rule, string.Join(' ', segments), segments));
                }
            }
        }
    }

    /// <summary>Explains the style of a token with these scopes, resolved by the theme this explainer was built with.</summary>
    public ThemeExplanation Explain(AttributedScopeStack scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var names = scopes.GetScopeNames();
        var levels = new AttributedScopeStack[names.Length];
        var node = scopes;
        for (var i = names.Length - 1; i >= 0; i--)
        {
            levels[i] = node;
            if (node.Parent is not { } parent)
            {
                break;
            }

            node = parent;
        }

        return new ThemeExplanation(
            Find(names, levels, static (level, parent) => GetForeground(level) != GetForeground(parent), static (selector, level, theme) => theme.ColorMap.GetId(selector.Rule.Foreground) == GetForeground(level)),
            Find(names, levels, static (level, parent) => GetFontStyle(level) != GetFontStyle(parent), static (selector, level, theme) => selector.Rule.FontStyle is not null && ParseFontStyle(selector.Rule.FontStyle) == GetFontStyle(level)));
    }

    private MatchedThemeRule? Find(string[] names, AttributedScopeStack[] levels, Func<AttributedScopeStack, AttributedScopeStack?, bool> changed, Func<Selector, AttributedScopeStack, Theme, bool> sets)
    {
        for (var depth = names.Length - 1; depth >= 0; depth--)
        {
            var level = levels[depth];
            var parent = depth > 0 ? levels[depth - 1] : null;
            if (parent is not null && !changed(level, parent))
            {
                continue;
            }

            Selector? best = null;
            foreach (var selector in _selectors)
            {
                if (sets(selector, level, _theme) && selector.Matches(names, depth) && (best is null || selector.CompareSpecificity(best) >= 0))
                {
                    best = selector;
                }
            }

            if (best is not null)
            {
                return new MatchedThemeRule(best.Index, best.Rule.Name, best.Text, names[depth], best.Rule.Foreground, best.Rule.FontStyle);
            }

            if (parent is null)
            {
                break;
            }
        }

        return null;
    }

    private static int GetForeground(AttributedScopeStack? scopes) => scopes is null ? -1 : EncodedTokenAttributes.GetForeground(scopes.TokenAttributes);

    private static FontStyle GetFontStyle(AttributedScopeStack? scopes) => scopes is null ? FontStyle.NotSet : EncodedTokenAttributes.GetFontStyle(scopes.TokenAttributes);

    private static FontStyle ParseFontStyle(string text)
    {
        var style = FontStyle.None;
        foreach (var segment in text.Split(' '))
        {
            style |= segment switch
            {
                "italic" => FontStyle.Italic,
                "bold" => FontStyle.Bold,
                "underline" => FontStyle.Underline,
                "strikethrough" => FontStyle.Strikethrough,
                _ => FontStyle.None,
            };
        }

        return style;
    }

    private sealed class Selector(int index, RawThemeRule rule, string text, string[] segments)
    {
        public int Index { get; } = index;

        public RawThemeRule Rule { get; } = rule;

        public string Text { get; } = text;

        private int Depth { get; } = segments[^1].Split('.').Length;

        // The last segment matches the scope at depth, the others its ancestors in order, '>' asking for the direct
        // parent.
        public bool Matches(string[] names, int depth)
        {
            if (!MatchesScope(names[depth], segments[^1]))
            {
                return false;
            }

            var position = depth - 1;
            var direct = false;
            for (var i = segments.Length - 2; i >= 0; i--)
            {
                var pattern = segments[i];
                if (pattern == ">")
                {
                    direct = true;
                    continue;
                }

                while (position >= 0 && !MatchesScope(names[position], pattern))
                {
                    if (direct)
                    {
                        return false;
                    }

                    position--;
                }

                if (position < 0)
                {
                    return false;
                }

                position--;
                direct = false;
            }

            return true;
        }

        // Deeper scope first, then more parent segments, then the later rule.
        public int CompareSpecificity(Selector other)
        {
            if (Depth != other.Depth)
            {
                return Depth - other.Depth;
            }

            if (segments.Length != other.SegmentCount)
            {
                return segments.Length - other.SegmentCount;
            }

            return Index - other.Index;
        }

        private int SegmentCount => segments.Length;

        private static bool MatchesScope(string scope, string pattern)
            => scope == pattern || (scope.Length > pattern.Length && scope.StartsWith(pattern, StringComparison.Ordinal) && scope[pattern.Length] == '.');
    }
}
