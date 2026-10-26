using System.Diagnostics;
using Tint.Grammars;
using Tint.Themes;

namespace Tint.Playground;

/// <summary>One highlight of the playground: the HTML, the tokens and how long each took.</summary>
/// <param name="Html">What <see cref="Highlighter.ToHtml(string)"/> wrote.</param>
/// <param name="Tokens">The tokens of the inspected theme, for the inspector.</param>
/// <param name="TokenizeTime">The time <see cref="Highlighter.Tokenize(string)"/> took.</param>
/// <param name="RenderTime">The time <see cref="Highlighter.ToHtml(string)"/> took (it tokenizes again, once per theme).</param>
public sealed record HighlightResult(string Html, TokenMap Tokens, TimeSpan TokenizeTime, TimeSpan RenderTime);

/// <summary>
/// The playground's highlighting: one registry with every grammar, each theme resolved once, and a small
/// most-recently-used cache of one <see cref="Highlighter"/> per grammar and themes.
/// </summary>
/// <remarks>Not thread-safe, like the highlighters it keeps; the browser runs it on its one thread.</remarks>
public sealed class PlaygroundEngine
{
    /// <summary>How many highlighters the cache keeps.</summary>
    public const int CacheCapacity = 8;

    private readonly GrammarRegistry _registry = new();
    private readonly Dictionary<string, ThemeOption> _themesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LanguageOption> _languagesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Theme> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThemeRuleExplainer> _explainers = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, Highlighter Highlighter)> _highlighters = new();

    /// <summary>Registers every grammar and lists the languages and themes.</summary>
    public PlaygroundEngine(IReadOnlyList<IGrammarSource> grammars, IReadOnlyList<IThemeSource> themes)
    {
        ArgumentNullException.ThrowIfNull(grammars);
        ArgumentNullException.ThrowIfNull(themes);
        foreach (var grammar in grammars)
        {
            _registry.Add(grammar);
        }

        Languages = LanguageCatalog.Build(grammars);
        Themes = ThemeCatalog.Build(themes);
        foreach (var language in Languages)
        {
            _languagesById[language.Id] = language;
        }

        foreach (var theme in Themes)
        {
            _themesById[theme.Id] = theme;
        }
    }

    /// <summary>The languages, sorted by label.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>The themes, dark ones first.</summary>
    public IReadOnlyList<ThemeOption> Themes { get; }

    /// <summary>How many highlighters are cached.</summary>
    public int CachedHighlighterCount => _highlighters.Count;

    /// <summary>Finds a language by id.</summary>
    public LanguageOption? FindLanguage(string? id) => id is not null && _languagesById.TryGetValue(id, out var language) ? language : null;

    /// <summary>Finds a theme by id.</summary>
    public ThemeOption? FindTheme(string? id) => id is not null && _themesById.TryGetValue(id, out var theme) ? theme : null;

    /// <summary>The resolved theme, built once.</summary>
    public Theme GetTheme(ThemeOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!_resolved.TryGetValue(option.Id, out var theme))
        {
            theme = Theme.FromSource(option.Source);
            _resolved[option.Id] = theme;
        }

        return theme;
    }

    /// <summary>The explainer of a theme's rules, built once.</summary>
    public ThemeRuleExplainer GetExplainer(ThemeOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!_explainers.TryGetValue(option.Id, out var explainer))
        {
            explainer = new ThemeRuleExplainer(option.Source, GetTheme(option));
            _explainers[option.Id] = explainer;
        }

        return explainer;
    }

    /// <summary>
    /// The highlighter of a language with one theme, or with a second theme written as CSS variables named after
    /// <see cref="GetVariableName"/>; cached.
    /// </summary>
    public Highlighter GetHighlighter(LanguageOption language, ThemeOption theme, ThemeOption? secondTheme = null)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(theme);
        var key = language.ScopeName + "|" + theme.Id + "|" + secondTheme?.Id;
        for (var node = _highlighters.First; node is not null; node = node.Next)
        {
            if (node.Value.Key == key)
            {
                _highlighters.Remove(node);
                _highlighters.AddFirst(node);
                return node.Value.Highlighter;
            }
        }

        var highlighter = secondTheme is null
            ? _registry.CreateHighlighter(language.ScopeName, GetTheme(theme))
            : _registry.CreateHighlighter(language.ScopeName, GetTheme(theme), [new KeyValuePair<string, Theme>(GetVariableName(secondTheme), GetTheme(secondTheme))]);
        _highlighters.AddFirst((key, highlighter));
        if (_highlighters.Count > CacheCapacity)
        {
            _highlighters.RemoveLast();
        }

        return highlighter;
    }

    /// <summary>
    /// Renders code with one or two themes and tokenizes it with <paramref name="inspectedTheme"/> (the first theme by
    /// default) for the inspector, timing each.
    /// </summary>
    public HighlightResult Highlight(LanguageOption language, ThemeOption theme, ThemeOption? secondTheme, string code, ThemeOption? inspectedTheme = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        var highlighter = GetHighlighter(language, theme, secondTheme);
        var tokenizer = inspectedTheme is null || inspectedTheme == theme ? highlighter : GetHighlighter(language, inspectedTheme);
        var start = Stopwatch.GetTimestamp();
        var tokens = tokenizer.Tokenize(code);
        var tokenized = Stopwatch.GetTimestamp();
        var html = highlighter.ToHtml(code);
        var rendered = Stopwatch.GetTimestamp();
        return new HighlightResult(html, new TokenMap(code, tokens), Stopwatch.GetElapsedTime(start, tokenized), Stopwatch.GetElapsedTime(tokenized, rendered));
    }

    /// <summary>The name of the CSS variables of a second theme: <c>dark</c> or <c>light</c>, after its kind.</summary>
    public static string GetVariableName(ThemeOption theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.Kind == ThemeKind.Light ? "light" : "dark";
    }

    /// <summary>
    /// The CSS that shows the second theme of a dual-theme render: inside a <c>prefers-color-scheme</c> media query for
    /// its kind, or under <paramref name="selectorPrefix"/> when one is given (the playground's preview toggle).
    /// </summary>
    public static string GetDualThemeCss(ThemeOption secondTheme, string? selectorPrefix = null, string preClass = "tint", string variablePrefix = "--tint-")
    {
        ArgumentNullException.ThrowIfNull(secondTheme);
        var name = variablePrefix + GetVariableName(secondTheme);
        var selector = selectorPrefix is null ? $".{preClass}, .{preClass} span" : $"{selectorPrefix} .{preClass}, {selectorPrefix} .{preClass} span";
        var indent = selectorPrefix is null ? "  " : string.Empty;
        var rule = $$"""
            {{indent}}{{selector}} {
            {{indent}}  color: var({{name}}) !important;
            {{indent}}  background-color: var({{name}}-bg) !important;
            {{indent}}  font-style: var({{name}}-font-style) !important;
            {{indent}}  font-weight: var({{name}}-font-weight) !important;
            {{indent}}  text-decoration: var({{name}}-text-decoration) !important;
            {{indent}}}
            """;
        if (selectorPrefix is not null)
        {
            return rule + "\n";
        }

        var scheme = secondTheme.Kind == ThemeKind.Light ? "light" : "dark";
        return $"@media (prefers-color-scheme: {scheme}) {{\n{rule}\n}}\n";
    }

    /// <summary>The page colors of a theme: its <c>editor.background</c> and <c>editor.foreground</c>, or its defaults.</summary>
    public (string Background, string Foreground) GetEditorColors(ThemeOption option)
    {
        var theme = GetTheme(option);
        var background = theme.UiColors.TryGetValue("editor.background", out var bg) && bg.Length > 0 ? bg : theme.ColorMap.GetColor(theme.Defaults.BackgroundId);
        var foreground = theme.UiColors.TryGetValue("editor.foreground", out var fg) && fg.Length > 0 ? fg : theme.ColorMap.GetColor(theme.Defaults.ForegroundId);
        return (background, foreground);
    }
}
